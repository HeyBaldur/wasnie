using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Ledger;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Ledger;

/// <summary>
/// Closes a departed payee's account: the commission they were owed and never paid, and any debt still
/// on their ledger, finished together in one transaction.
///
/// ★★ WHY BOTH HALVES IN ONE COMMAND. The two rows that exist today — the ones that started all of this
/// — have a ledger balance of exactly zero and nothing but unsettled commission. A closure that only
/// touched the ledger would be a button that cannot close the cases it was built for. And to the person
/// doing it, closing an account is ONE decision; splitting it would leave them guessing why some
/// accounts can be closed and others cannot.
///
/// ★ WASNIE RECORDS THE DECISION; IT DOES NOT MAKE IT AND DOES NOT MOVE MONEY. Whether the commission
/// was paid with a final paycheck or written off happens in payroll, finance and legal, with data this
/// system does not hold. What happens here is that the decision stops being invisible.
///
/// ★ AND IT IS ONE-WAY. Credits reach a terminal state and the ledger is append-only, so there is no
/// undo — only a new, separate decision. That is why the ceremony in front of this is the one
/// `Mark as paid` uses, and why <see cref="Permission.LedgerCloseAccount"/> is its own key.
/// </summary>
public sealed class CloseTerminatedAccountHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ICurrentUserService currentUser,
    ITenantContext tenantContext,
    IClock clock,
    IGuidGenerator guid)
    : IRequestHandler<CloseTerminatedAccountCommand, Result<CloseTerminatedAccountResult>>
{
    public async Task<Result<CloseTerminatedAccountResult>> Handle(
        CloseTerminatedAccountCommand request, CancellationToken cancellationToken)
    {
        // ★ NOT Ledger.Adjust. An adjustment can be compensated by another; this cannot be undone.
        await authorizationService.RequireAsync(Permission.LedgerCloseAccount, cancellationToken);

        var tenantId = tenantContext.TenantId;
        var actor = currentUser.Email ?? currentUser.UserId
            ?? throw new DomainException("Closing an account requires an authenticated user.");
        var now = clock.UtcNowOffset;

        if (string.IsNullOrWhiteSpace(request.Note))
            return Fail("A closure note is required.");
        if (string.IsNullOrWhiteSpace(request.Currency))
            return Fail("A currency is required.");

        var currency = request.Currency.Trim().ToUpperInvariant();

        var payee = await db.Payees.FirstOrDefaultAsync(p => p.Id == request.PayeeId, cancellationToken);
        if (payee is null) return Fail("Payee not found.");

        // ── Fail-closed on the two states that must not be closed ─────────────────────────────────
        // Both are checked BEFORE anything is read or written, and both are plain failures rather than
        // conflicts: they are not races, they are the wrong account.
        if (payee.Status != PayeeStatus.Terminated)
            return Fail("Only a terminated payee's account can be closed.");
        if (payee.AccountClosedAt is not null)
            return Fail("This payee's account is already closed.");

        // ── ★★ STRICT SET CONCURRENCY ─────────────────────────────────────────────────────────────
        // What the user saw, against what is there now — by ID and by AMOUNT, in both directions.
        //
        // Comparing totals would not do. Two credits can change while the sum stays put (€500 arrives
        // as €500 is consumed) and the set is then a different set with the same total. And a departed
        // payee's set is genuinely unstable: the product deliberately allows a credit to arrive after
        // someone leaves, and one already did, 56 seconds after a termination.
        var outstanding = await db.Credits
            .Where(c => c.PayeeId == request.PayeeId
                     && c.SupersededAt == null
                     && c.ConsumedAt == null
                     && c.ClosedAt == null)
            .ToListAsync(cancellationToken);

        // One closure closes one currency — the queue is per (payee, currency) because Wasnie holds no
        // exchange rates, and a closure that spanned currencies would have no single total to confirm.
        outstanding = outstanding.Where(c => c.CreditedAmount.Currency == currency).ToList();

        var shown = request.Credits.ToDictionary(c => c.CreditId, c => c.Amount);

        foreach (var credit in outstanding)
        {
            if (!shown.TryGetValue(credit.Id, out var shownAmount))
            {
                throw new AccountSnapshotStaleException(
                    AccountSnapshotStaleException.Reasons.CreditAppeared,
                    $"Credit {credit.Id} is outstanding on this account but was not in the confirmation.");
            }

            if (shownAmount != credit.CreditedAmount.Amount)
            {
                throw new AccountSnapshotStaleException(
                    AccountSnapshotStaleException.Reasons.CreditAmountChanged,
                    $"Credit {credit.Id} is {credit.CreditedAmount.Amount}, not the {shownAmount} shown.");
            }
        }

        var stillOutstanding = outstanding.Select(c => c.Id).ToHashSet();
        var vanished = shown.Keys.FirstOrDefault(id => !stillOutstanding.Contains(id));
        if (vanished != Guid.Empty)
        {
            throw new AccountSnapshotStaleException(
                AccountSnapshotStaleException.Reasons.CreditDisappeared,
                $"Credit {vanished} is no longer outstanding — it was paid, superseded or already closed.");
        }

        // ── ★ AND THE CREDIT MUST NOT BE SITTING IN AN UNPAID PAYOUT ──────────────────────────────
        // ConsumedAt is only set when a payout is PAID, so a credit inside a Calculated or Approved
        // payout still looks outstanding here. Closing it would leave that payout holding a terminal
        // credit, and marking it paid later would try to consume something already settled — a double
        // settlement, discovered at the worst possible moment. Refused up front, with a name to look at.
        if (outstanding.Count > 0)
        {
            var creditIds = outstanding.Select(c => c.Id).ToList();
            var blockingPayout = await db.CompensationPayouts
                .Where(p => p.PayeeId == request.PayeeId
                         && p.Status != CompensationPayoutStatus.Paid
                         && p.Lines.Any(l => creditIds.Contains(l.CreditId)))
                .Select(p => new { p.Id, p.Status })
                .FirstOrDefaultAsync(cancellationToken);

            if (blockingPayout is not null)
            {
                return Fail(
                    $"This account has commission inside payout {blockingPayout.Id} ({blockingPayout.Status}). "
                    + "Resolve that payout first — closing it here would settle the same money twice.");
            }
        }

        // ── The ledger side ───────────────────────────────────────────────────────────────────────
        var balance = await db.PayeeBalances
            .FirstOrDefaultAsync(b => b.PayeeId == request.PayeeId && b.Currency == currency, cancellationToken);

        var balanceBefore = balance?.Balance.Amount ?? 0m;

        // Null in the request means "the screen showed no balance row at all", which is a different
        // fact from a balance of zero — so the two are compared as they were shown, not coerced.
        var expected = request.ExpectedBalance ?? 0m;
        if (expected != balanceBefore)
        {
            throw new AccountSnapshotStaleException(
                AccountSnapshotStaleException.Reasons.BalanceChanged,
                $"The balance is {balanceBefore} {currency}, not the {expected} shown.");
        }

        if (outstanding.Count == 0 && balanceBefore == 0m)
            return Fail("There is nothing to close on this account.");

        try
        {
            // ── Close the credits ─────────────────────────────────────────────────────────────────
            var creditReason = request.Resolution == AccountClosureResolution.WrittenOff
                ? CreditClosureReason.WrittenOff
                : CreditClosureReason.ExternalSettlement;

            foreach (var credit in outstanding)
                credit.Close(creditReason, request.Note, actor, now, guid.NewGuid());

            // ── Bring the ledger to zero, with the type the resolution demands ────────────────────
            // ★ NEVER A GENERIC ADJUSTMENT. The enum already separates "recovered elsewhere" from "we
            // ate the loss" from "we paid them what we owed" precisely so a CFO can total each without
            // mining free text; collapsing them here would throw that away at the only moment it is
            // ever decided.
            if (balanceBefore != 0m)
            {
                var entryType = LedgerTypeFor(request.Resolution, balanceBefore);
                var entry = PayeeLedgerEntry.CreateManualAdjustment(
                    tenantId, request.PayeeId, entryType,
                    Money.Of(Math.Abs(balanceBefore), currency),
                    request.Note, actor, guid.NewGuid(), now, guid.NewGuid());

                balance!.Apply(entry, now);
                db.PayeeLedgerEntries.Add(entry);
            }

            payee.MarkAccountClosed(actor, now);

            // One SaveChanges: the credits, the entry, the balance and the payee flag are one fact.
            await db.SaveChangesAsync(cancellationToken);

            var closedTotal = outstanding.Sum(c => c.CreditedAmount.Amount);
            var balanceAfter = balance?.Balance.Amount ?? 0m;

            // ── ★ THE AUDIT ROW IS WRITTEN BY THE PIPELINE, NOT HERE, AND THAT IS DELIBERATE ─────
            // CloseTerminatedAccountCommand is IMoneyCriticalCommand, so AuditBehavior wraps this whole
            // handler in one transaction and writes the entry inside it: if the audit fails, the
            // closure rolls back. Destroying a person's claim without a record of who did it is not a
            // degraded outcome. The entry's Metadata — the closed credit ids with their amounts, the
            // resolution, the balance either side — comes off the command, which by this point the
            // strict set check above has PROVEN to be exactly what was closed.
            //
            // Writing a second row here would double-count every closure in the audit log.
            return Result<CloseTerminatedAccountResult>.Success(new CloseTerminatedAccountResult(
                outstanding.Count, closedTotal, balanceBefore, balanceAfter, currency));
        }
        catch (DomainException ex)
        {
            return Fail(ex.Message);
        }
    }

    /// <summary>
    /// Which ledger type zeroes this balance, given what the person decided happened.
    ///
    /// ★ THE SIGN PICKS BETWEEN TWO OF THEM. A NEGATIVE balance is a debt the payee owed: settling it
    /// externally is <c>ExternalSettlementCredit</c> (payroll recovered it) and absorbing it is
    /// <c>WriteOffCredit</c>. A POSITIVE balance is money the company owed THEM, and there is only one
    /// honest way for that to reach zero — <c>FinalSettlementDebit</c>, cash actually transferred.
    /// "Writing off" money you owe somebody else is not a write-off; it is not paying them, and the
    /// ledger has no type for that because the product does not do it.
    /// </summary>
    private static LedgerTransactionType LedgerTypeFor(AccountClosureResolution resolution, decimal balance)
    {
        if (balance > 0m)
            return LedgerTransactionType.FinalSettlementDebit;

        return resolution == AccountClosureResolution.WrittenOff
            ? LedgerTransactionType.WriteOffCredit
            : LedgerTransactionType.ExternalSettlementCredit;
    }

    private static Result<CloseTerminatedAccountResult> Fail(string error) =>
        Result<CloseTerminatedAccountResult>.Failure(error);
}
