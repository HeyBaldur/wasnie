using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Commands.PayRuns;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.PayRuns;

public sealed class MarkPayRunPaidHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuditService audit,
    IPayRunSettlementService settlementService,
    ILogger<MarkPayRunPaidHandler> logger)
    : IRequestHandler<MarkPayRunPaidCommand, Result<PaymentBlockResult?>>
{
    public async Task<Result<PaymentBlockResult?>> Handle(MarkPayRunPaidCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsMarkPaid, cancellationToken);

        var payRun = await db.PayRuns
            .FirstOrDefaultAsync(r => r.Id == request.PayRunId, cancellationToken);

        if (payRun is null)
            return Result<PaymentBlockResult?>.Failure("Pay run not found.");

        try
        {
            var actor = currentUser.Email ?? currentUser.UserId ?? "system";
            var now = clock.UtcNowOffset;
            var allTenantId = payRun.TenantId;

            // Capture overlapping Approved/Paid runs before transition (for audit).
            var overlappingIds = await db.PayRuns
                .Where(r => r.Id != request.PayRunId
                         && (r.Status == PayRunStatus.Approved || r.Status == PayRunStatus.Paid)
                         && r.PeriodStart <= payRun.PeriodEnd
                         && r.PeriodEnd >= payRun.PeriodStart)
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);

            // Load payouts with Lines so we can check credits and propagate Paid to transactions.
            var payouts = await db.CompensationPayouts
                .Include(p => p.Lines)
                .Where(p => p.PayRunId == request.PayRunId
                         && p.Status == CompensationPayoutStatus.Approved)
                .ToListAsync(cancellationToken);

            // Batch-load ALL credits for this run's payouts (including Superseded — we need
            // to read their state to detect double-payment attempts).
            var allCreditIds = payouts.SelectMany(p => p.Lines.Select(l => l.CreditId)).Distinct().ToList();

            Dictionary<Guid, Domain.Compensation.Credits.Credit> creditById = [];
            Dictionary<Guid, Domain.Compensation.Transactions.CompensationTransaction> txById = [];

            if (allCreditIds.Count > 0)
            {
                var allCredits = await db.Credits
                    .IgnoreQueryFilters()
                    .Where(c => allCreditIds.Contains(c.Id) && c.TenantId == allTenantId)
                    .ToListAsync(cancellationToken);

                creditById = allCredits.ToDictionary(c => c.Id);

                // ── ANTI-DOUBLE-PAY GUARD ─────────────────────────────────────────
                // Before moving any money, verify no credit in this run is already consumed
                // by another payout. Payment order must not matter: if a bill was paid in any
                // prior payout, this entire run is blocked until the conflict is resolved.
                var alreadyConsumed = allCredits
                    .Where(c => c.ConsumedAt is not null)
                    .ToList();

                if (alreadyConsumed.Count > 0)
                {
                    // Load transaction references and consuming payout periods for the error message.
                    var consumedTxIds = alreadyConsumed.Select(c => c.TransactionId).Distinct().ToList();
                    var consumingPayoutIds = alreadyConsumed
                        .Where(c => c.ConsumedByPayoutId.HasValue)
                        .Select(c => c.ConsumedByPayoutId!.Value).Distinct().ToList();

                    var txRefs = await db.CompensationTransactions
                        .IgnoreQueryFilters()
                        .Where(t => consumedTxIds.Contains(t.Id) && t.TenantId == allTenantId)
                        .Select(t => new { t.Id, t.ReferenceNumber })
                        .ToListAsync(cancellationToken);

                    var consumingPayouts = await db.CompensationPayouts
                        .IgnoreQueryFilters()
                        .Where(p => consumingPayoutIds.Contains(p.Id) && p.TenantId == allTenantId)
                        .ToListAsync(cancellationToken);

                    var txRefById = txRefs.ToDictionary(t => t.Id, t => t.ReferenceNumber);
                    var consumingPayoutPeriod = consumingPayouts
                        .ToDictionary(p => p.Id, p => (Start: p.Period.Start.ToString("yyyy-MM-dd"), End: p.Period.End.ToString("yyyy-MM-dd")));

                    var conflictItems = alreadyConsumed
                        .Select(c =>
                        {
                            var txRef = c.TransactionId != Guid.Empty && txRefById.TryGetValue(c.TransactionId, out var r) ? r : c.TransactionId.ToString();
                            var payoutId = c.ConsumedByPayoutId ?? Guid.Empty;
                            consumingPayoutPeriod.TryGetValue(payoutId, out var period);
                            return new PaymentConflictItem(txRef, payoutId, period.Start ?? "?", period.End ?? "?");
                        })
                        .DistinctBy(x => x.TransactionReference)
                        .ToList();

                    var conflictSummary = string.Join("; ", conflictItems.Take(5).Select(c =>
                        $"{c.TransactionReference} → payout {c.PaidInPayoutId} ({c.PaidInPayoutPeriodStart} – {c.PaidInPayoutPeriodEnd})"));

                    logger.LogWarning(
                        "PayRun {PayRunId} payment BLOCKED — {Count} credits already consumed. Conflicts: {Conflicts}",
                        request.PayRunId, alreadyConsumed.Count, conflictSummary);

                    await audit.LogAsync(new AuditEntry(
                        TenantId: allTenantId,
                        Action: AuditActions.PaymentBlockedDoublePayment,
                        ResourceType: "PayRun",
                        ResourceId: request.PayRunId.ToString(),
                        ActorUserId: currentUser.UserId ?? actor,
                        ActorEmail: actor,
                        Metadata: new Dictionary<string, string>
                        {
                            ["blockedCreditCount"] = alreadyConsumed.Count.ToString(),
                            ["conflicts"]          = conflictSummary,
                        }), cancellationToken);

                    return Result<PaymentBlockResult?>.Success(new PaymentBlockResult(alreadyConsumed.Count, conflictItems));
                }

                // Load transactions for consumption (only Calculated ones — Paid ones would indicate
                // a prior payment that didn't properly consume the credit, and would have been
                // caught by the guard above if a credit was consumed, or signals a data anomaly).
                var allTxIds = allCredits
                    .Where(c => c.SupersededAt == null)
                    .Select(c => c.TransactionId).Distinct().ToList();

                if (allTxIds.Count > 0)
                {
                    var allTxs = await db.CompensationTransactions
                        .IgnoreQueryFilters()
                        .Where(t => allTxIds.Contains(t.Id)
                                 && t.TenantId == allTenantId
                                 && t.Status == CompensationTransactionStatus.Calculated)
                        .ToListAsync(cancellationToken);

                    txById = allTxs.ToDictionary(t => t.Id);
                }
            }

            payRun.MarkPaid(actor, now, guid.NewGuid());

            var totalCreditsConsumed = 0;
            var totalTxsPaid = 0;

            foreach (var payout in payouts)
            {
                payout.MarkPaid(actor, now);

                var consumedTxIds = new HashSet<Guid>();
                foreach (var line in payout.Lines)
                {
                    if (!creditById.TryGetValue(line.CreditId, out var credit))
                        continue;
                    if (credit.SupersededAt is not null)
                        continue;

                    // Consume() throws DomainException if already consumed — the guard above ensures
                    // we never reach here with a consumed credit. This is a safety net only.
                    credit.Consume(payout.Id, now, Guid.NewGuid());
                    totalCreditsConsumed++;
                    consumedTxIds.Add(credit.TransactionId);
                }

                foreach (var txId in consumedTxIds)
                {
                    if (txById.TryGetValue(txId, out var tx))
                    {
                        tx.MarkPaid(actor, now, Guid.NewGuid());
                        totalTxsPaid++;
                    }
                }
            }

            // ── CLAWBACK SETTLEMENT ───────────────────────────────────────────
            // Withhold any outstanding balance from what is about to be paid. Staged only — it
            // rides the SAME SaveChanges as the credit consumption above, so a payment can never
            // move without its settlement, nor a settlement without its payment.
            var settlements = await settlementService.StageSettlementsAsync(
                allTenantId, request.PayRunId, payouts, actor, now, cancellationToken);

            logger.LogInformation(
                "PayRun {PayRunId} marked Paid: {PayoutCount} payouts, {CreditCount} credits consumed, " +
                "{TxCount} transactions marked Paid, {SettlementCount} clawback settlements applied.",
                request.PayRunId, payouts.Count, totalCreditsConsumed, totalTxsPaid, settlements.Count);

            // Roll-ups don't change amounts on MarkPaid but UpdateRollUps keeps state consistent.
            var allRunPayouts = await db.CompensationPayouts
                .Where(p => p.PayRunId == request.PayRunId
                         && p.Status != CompensationPayoutStatus.Disputed)
                .ToListAsync(cancellationToken);

            payRun.UpdateRollUps(allRunPayouts);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Two sources now: a concurrent payment consumed the same credit, OR a manual ledger
                // adjustment changed a payee's balance after we computed the withholding. Either way
                // the whole run is aborted rather than partially applied — a retry recomputes the
                // withholding against the balance as it stands then.
                logger.LogError(ex,
                    "PayRun {PayRunId} payment blocked by concurrent update — a shared credit was consumed " +
                    "or a payee balance changed while the run was being settled.",
                    request.PayRunId);
                return Result<PaymentBlockResult?>.Failure(
                    "Payment could not be processed: a concurrent payment consumed one or more credits in this run, "
                    + "or a balance adjustment landed while it was being settled. Please refresh and try again.");
            }

            await audit.LogAsync(new AuditEntry(
                TenantId: allTenantId,
                Action: AuditActions.PayoutCreditsConsumed,
                ResourceType: "PayRun",
                ResourceId: request.PayRunId.ToString(),
                ActorUserId: currentUser.UserId ?? actor,
                ActorEmail: actor,
                Metadata: new Dictionary<string, string>
                {
                    ["payoutsInRun"]     = payouts.Count.ToString(),
                    ["creditsConsumed"]  = totalCreditsConsumed.ToString(),
                    ["transactionsPaid"] = totalTxsPaid.ToString(),
                }), cancellationToken);

            if (overlappingIds.Count > 0)
            {
                await audit.LogAsync(new AuditEntry(
                    TenantId: allTenantId,
                    Action: AuditActions.PayRunPaidWithOverlap,
                    ResourceType: "PayRun",
                    ResourceId: request.PayRunId.ToString(),
                    ActorUserId: currentUser.UserId ?? actor,
                    ActorEmail: actor,
                    Metadata: new Dictionary<string, string>
                    {
                        ["overlappingPayRunIds"] = string.Join(",", overlappingIds),
                        ["overlapCount"]         = overlappingIds.Count.ToString(),
                    }), cancellationToken);
            }

            return Result<PaymentBlockResult?>.Success(null);
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            return Result<PaymentBlockResult?>.Failure(ex.Message);
        }
    }
}
