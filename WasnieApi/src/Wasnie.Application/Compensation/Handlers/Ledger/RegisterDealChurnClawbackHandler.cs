using System.Globalization;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Ledger;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Calculation;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Ledger;

/// <summary>
/// Turns a lost deal whose commission was already PAID into a proportional debt (Origin = System,
/// ClawbackDebit). This is the only automatic writer of debt in Wasnie, so every rule it obeys is
/// spelled out here rather than left to the reader:
///
/// 1. EVENT TIME vs BOOKING TIME. The CRM lets a person record today that a deal died in March.
///    <c>EventDate</c> (the CRM loss date) drives the formula and is stored as evidence; the entry is
///    BOOKED at <c>clock.UtcNowOffset</c>, i.e. in the currently open period. A new debit is never
///    injected into a period that has already been reconciled and paid — see
///    <see cref="PayeeLedgerEntry.EventDate"/>. Nothing here ever passes EventDate as the booking date.
///
/// 2. CONCURRENCY. The webhook-driven sync can fire while finance is closing a pay run. The debit is
///    applied to <see cref="PayeeBalance"/>, whose RowVersion is a real SQL rowversion, so a balance that
///    moved under us fails the write instead of overwriting it. On conflict we RE-READ the balance,
///    RE-APPLY our entries on top of the fresh figure and retry. The outcome is therefore always one of
///    "the debit made it into the run" or "the debit waits for the next run" — never lost, never double.
///
/// 3. NEGATIVE BALANCE IS ALLOWED. A 400 clawback against a 100 balance leaves −300, which carries over
///    and nets against future commissions (bounded only by each plan's cap at settlement). There is no
///    floor at zero: a floor would reward timing the churn against an empty account, and the ledger's job
///    is to record what is true — earned 100, owes 400, balance −300.
///
/// 4. IDEMPOTENCY. The reverse reconciler re-sees a lost deal on EVERY sync. A transaction that already
///    has a churn debit is a no-op, and the unique filtered index (SourceTransactionId, SourcePlanId)
///    backs that check at the database level, where a read-then-write check cannot reach.
///
/// It does NOT touch the credits or the transaction. That is the whole point of it being a separate
/// command: the payment happened, and history is corrected by a new entry, never by rewriting the old one.
/// </summary>
public sealed class RegisterDealChurnClawbackHandler(
    IApplicationDbContext db,
    IClock clock,
    IGuidGenerator guid)
    : IRequestHandler<RegisterDealChurnClawbackCommand, Result<DealChurnClawbackDto>>
{
    /// <summary>Actor stamped on the entries. No human chose these numbers, and the ledger says so.</summary>
    public const string SystemActor = "system";

    /// <summary>
    /// Re-read/retry budget for the OCC conflict. Three because the contention window is one balance row
    /// touched by one pay run; an unbounded loop against a permanently contended row would be a hang.
    /// </summary>
    private const int MaxAttempts = 3;

    public async Task<Result<DealChurnClawbackDto>> Handle(
        RegisterDealChurnClawbackCommand request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters + explicit TenantId throughout: the trigger runs from a background sync, so it
        // cannot rely on an ambient tenant. The predicate IS the tenant boundary here.
        var tx = await db.CompensationTransactions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                t => t.Id == request.TransactionId && t.TenantId == request.TenantId, cancellationToken);

        if (tx is null)
            return Result<DealChurnClawbackDto>.Failure("Transaction not found.");

        // Paid only. A Calculated commission has not left the company, so it is reverted (credits
        // superseded), not clawed back — that is the other command, and it stays the other command.
        if (tx.Status != CompensationTransactionStatus.Paid)
            return Result<DealChurnClawbackDto>.Failure(
                $"A churn clawback applies to a PAID commission. Transaction {tx.ReferenceNumber} is {tx.Status}; " +
                "an unpaid commission is corrected by reverting it, not by creating a debt.");

        if (tx.PayeeId is not { } payeeId)
            return Result<DealChurnClawbackDto>.Failure(
                "This transaction has no payee, so there is no ledger to charge.");

        var already = await db.PayeeLedgerEntries
            .IgnoreQueryFilters()
            .AnyAsync(
                e => e.TenantId == request.TenantId
                  && e.SourceTransactionId == tx.Id
                  && e.SourceType == LedgerSourceType.DealChurn,
                cancellationToken);

        if (already)
            return Result<DealChurnClawbackDto>.Success(
                new DealChurnClawbackDto(tx.Id, DealChurnClawbackDto.OutcomeAlreadyPosted, []));

        // What the company ACTUALLY paid for this transaction: credits consumed by a Paid payout. Not the
        // credited amount — a credit that was calculated but never paid was never money out the door, and
        // clawing it back would invent a debt.
        var paidCredits = await db.Credits
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == request.TenantId
                     && c.TransactionId == tx.Id
                     && c.SupersededAt == null
                     && c.ConsumedAt != null)
            .Select(c => new
            {
                c.PlanId,
                Amount = c.CreditedAmount.Amount,
                Currency = c.CreditedAmount.Currency,
            })
            .ToListAsync(cancellationToken);

        if (paidCredits.Count == 0)
            return Result<DealChurnClawbackDto>.Success(
                new DealChurnClawbackDto(tx.Id, DealChurnClawbackDto.OutcomeNothingPaid, []));

        var planIds = paidCredits.Select(c => c.PlanId).Distinct().ToList();
        var maturationByPlan = (await db.CompensationPlans
                .IgnoreQueryFilters()
                .Where(p => p.TenantId == request.TenantId && planIds.Contains(p.Id))
                .Select(p => new { p.Id, p.ClawbackMaturationDays })
                .ToListAsync(cancellationToken))
            .ToDictionary(p => p.Id, p => p.ClawbackMaturationDays);

        // The clawback is OPT-IN per plan. A plan with no maturation window has not switched the feature
        // on, and the trigger stays inert for it. That is a configuration state, not a failure.
        var withPolicy = paidCredits
            .Where(c => maturationByPlan.TryGetValue(c.PlanId, out var m) && m is > 0)
            .ToList();

        if (withPolicy.Count == 0)
            return Result<DealChurnClawbackDto>.Success(
                new DealChurnClawbackDto(tx.Id, DealChurnClawbackDto.OutcomeNoPolicy, []));

        // How long the deal really lived: from the close (won) date Wasnie credited to the CRM's loss date.
        // EventDate, never DetectedAt — sync latency must not shrink someone's debt or inflate it.
        var daysActive = ClawbackCalculator.DaysActiveBetween(tx.TransactionDate, request.EventDate);

        var now = clock.UtcNowOffset;
        var staged = new List<StagedDebit>();

        try
        {
            // One entry per (plan, currency): MaturationDays is a plan setting, so a transaction credited
            // under two plans has two different windows and must not be collapsed into one unexplainable row.
            foreach (var group in withPolicy.GroupBy(c => (c.PlanId, c.Currency)))
            {
                var maturationDays = maturationByPlan[group.Key.PlanId]!.Value;
                var commissionPaid = Money.Of(group.Sum(c => c.Amount), group.Key.Currency);

                var clawback = ClawbackCalculator.Proportional(commissionPaid, daysActive, maturationDays);
                if (clawback.Amount <= 0m)
                    continue; // deal outlived its window — the payee keeps every cent (the floor, in action)

                var balance = await GetOrOpenBalanceAsync(
                    request.TenantId, payeeId, group.Key.Currency, now, cancellationToken);

                var entry = PayeeLedgerEntry.CreateSystemEntry(
                    request.TenantId,
                    payeeId,
                    LedgerTransactionType.ClawbackDebit,
                    clawback,
                    $"Deal {request.ExternalDealId ?? tx.ReferenceNumber} was lost on {request.EventDate:yyyy-MM-dd} " +
                    $"after {daysActive} of {maturationDays} maturation days; " +
                    $"{clawback} of the {commissionPaid} paid is unearned.",
                    LedgerSourceType.DealChurn,
                    SystemActor,
                    guid.NewGuid(),
                    now, // ← the ACCOUNTING date: the open period, not request.EventDate. Blindaje 1.
                    guid.NewGuid(),
                    sourceTransactionId: tx.Id,
                    sourceExternalDealId: request.ExternalDealId,
                    sourceCommissionAmount: commissionPaid.Amount,
                    daysActive: daysActive,
                    maturationDays: maturationDays,
                    sourcePlanId: group.Key.PlanId,
                    eventDate: request.EventDate);

                // Apply BEFORE staging: a currency mismatch throws here and nothing is written.
                balance.Apply(entry, now);
                db.PayeeLedgerEntries.Add(entry);

                staged.Add(new StagedDebit(balance, entry, maturationDays, commissionPaid.Amount));
            }
        }
        catch (DomainException ex)
        {
            return Result<DealChurnClawbackDto>.Failure(ex.Message);
        }

        if (staged.Count == 0)
            return Result<DealChurnClawbackDto>.Success(
                new DealChurnClawbackDto(tx.Id, DealChurnClawbackDto.OutcomeMatured, []));

        db.AuditLogs.Add(AuditLog.Create(
            tenantId: request.TenantId,
            timestampUtc: now.UtcDateTime,
            actorUserId: SystemActor,
            actorEmail: SystemActor,
            action: AuditActions.DealChurnClawbackPosted,
            resourceType: ResourceTypes.Payee,
            resourceId: payeeId.ToString(),
            resourceDisplayName: tx.ReferenceNumber,
            beforeJson: null,
            afterJson: JsonSerializer.Serialize(new
            {
                transactionId = tx.Id,
                externalDealId = request.ExternalDealId,
                // Both dates, side by side, on purpose: the audit row itself shows that the event is older
                // than the booking and that we booked forward, never backward.
                eventDate = request.EventDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                bookedAt = now.UtcDateTime,
                dealClosedOn = tx.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                daysActive,
                entries = staged.Select(s => new
                {
                    ledgerEntryId = s.Entry.Id,
                    planId = s.Entry.SourcePlanId,
                    maturationDays = s.MaturationDays,
                    commissionPaid = s.Paid,
                    clawback = s.Entry.Amount.Amount,
                    currency = s.Entry.Amount.Currency,
                }),
            })));

        await SaveWithConcurrencyRetryAsync(staged, now, cancellationToken);

        return Result<DealChurnClawbackDto>.Success(new DealChurnClawbackDto(
            tx.Id,
            DealChurnClawbackDto.OutcomeDebited,
            staged.Select(s => new DealChurnClawbackEntryDto(
                s.Entry.Id,
                s.Entry.SourcePlanId!.Value,
                s.Entry.Amount.Amount,
                s.Entry.Amount.Currency,
                s.Paid,
                daysActive,
                s.MaturationDays,
                request.EventDate,
                s.Entry.CreatedAt,
                s.Balance.Balance.Amount)).ToList()));
    }

    /// <summary>
    /// The balance row is opened on first touch. The unique index on (tenant, payee, currency) is what
    /// stops two concurrent first-touches from creating two rows that would each net against half the debt.
    /// </summary>
    private async Task<PayeeBalance> GetOrOpenBalanceAsync(
        Guid tenantId, Guid payeeId, string currency, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var balance = await db.PayeeBalances
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                b => b.TenantId == tenantId && b.PayeeId == payeeId && b.Currency == currency,
                cancellationToken);

        if (balance is not null)
            return balance;

        balance = PayeeBalance.Open(tenantId, payeeId, currency, guid.NewGuid(), now);
        db.PayeeBalances.Add(balance);
        return balance;
    }

    /// <summary>
    /// Blindaje 2 in code. If the balance moved between our read and our write — the classic case being a
    /// pay run settling the very debt we are adding to — SQL rejects the UPDATE on its rowversion and
    /// NOTHING is committed (one SaveChanges, one transaction: our ledger rows are still merely Added).
    /// We then re-read the balance, re-apply our entries on top of the winner's figure, and try again.
    ///
    /// Re-read, not merge: the fresh row already contains the other writer's effect, so re-applying our own
    /// signed amounts to it yields the sum of BOTH — which is exactly the invariant
    /// "balance == sum of entries". Recomputing from our stale figure would erase the other writer.
    ///
    /// The re-read is two reloads, not one: <see cref="PayeeBalance.Balance"/> is an OWNED type and does
    /// not come back with its owner. See the comment at the call site.
    /// </summary>
    private async Task SaveWithConcurrencyRetryAsync(
        List<StagedDebit> staged, DateTimeOffset now, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                foreach (var group in staged.GroupBy(s => s.Balance).ToList())
                {
                    var stale = group.Key;
                    if (db.PayeeBalances.Entry(stale).State == EntityState.Added)
                        continue; // never written yet — nothing stale to re-read

                    // TWO reloads, and both are load-bearing. Reloading the owner refreshes its scalars
                    // and its RowVersion, but NOT the owned Money — that is a separate tracked entry, so
                    // the stale figure would survive and our delta would land on top of itself, silently
                    // DOUBLING the debt. (Observed against real SQL before this line existed; the race
                    // test is what holds it shut.)
                    var staleEntry = db.PayeeBalances.Entry(stale);
                    await staleEntry.ReloadAsync(cancellationToken);
                    var ownedMoney = staleEntry.Reference(b => b.Balance).TargetEntry;
                    if (ownedMoney is not null)
                        await ownedMoney.ReloadAsync(cancellationToken);

                    foreach (var s in group)
                        stale.Apply(s.Entry, now);
                }
            }
        }
    }

    /// <summary>One debit on its way to the database, plus the inputs the report needs to explain it.</summary>
    private sealed class StagedDebit(PayeeBalance balance, PayeeLedgerEntry entry, int maturationDays, decimal paid)
    {
        public PayeeBalance Balance { get; } = balance;
        public PayeeLedgerEntry Entry { get; } = entry;
        public int MaturationDays { get; } = maturationDays;
        public decimal Paid { get; } = paid;
    }
}
