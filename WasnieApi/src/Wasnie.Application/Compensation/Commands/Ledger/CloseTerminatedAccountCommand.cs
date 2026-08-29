using System.Globalization;
using MediatR;
using Wasnie.Domain.Audit;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Commands.Ledger;

/// <summary>
/// Close a departed payee's account: mark what they were owed as finished, and bring any ledger debt
/// to zero — in one transaction, or not at all.
///
/// ★★ IT CARRIES WHAT THE USER SAW, NOT JUST WHO TO CLOSE. <see cref="Credits"/> is the exact set of
/// credit ids and amounts the modal displayed, and <see cref="ExpectedBalance"/> the debt beside them.
/// The handler refuses if reality has moved (see <c>CloseTerminatedAccountHandler</c>): a departed
/// payee's credit set is NOT stable — the product deliberately allows a credit to arrive after someone
/// leaves, and one already did, 56 seconds after a termination
/// (docs/DIAG_POL-8554_PAYOUT_Y_CREDITOS_INVENTADOS.md).
///
/// ★ MONEY-CRITICAL. <see cref="IMoneyCriticalCommand"/> puts the business write and the audit row in
/// the same transaction: if the audit fails, the closure rolls back. Destroying a person's claim
/// without a record of who did it is not a degraded outcome, it is an unacceptable one.
/// </summary>
/// <param name="Resolution">
/// What actually happened to the money. Drives BOTH the credits' closure reason and the ledger entry
/// type — never a generic adjustment, because "recovered elsewhere" and "we ate it" have to be
/// totallable apart.
/// </param>
/// <param name="Note">Why, in the closer's own words. Required, and stored on every credit AND the entry.</param>
/// <param name="Credits">Every outstanding credit the user was shown, with the amount they were shown.</param>
/// <param name="ExpectedBalance">
/// The ledger balance the user was shown, signed as stored. Null when the modal showed no balance row
/// at all — which is not the same as zero, and the handler treats the two differently.
/// </param>
/// <param name="Currency">The currency of this account. One closure closes one currency.</param>
public sealed record CloseTerminatedAccountCommand(
    Guid PayeeId,
    string Currency,
    AccountClosureResolution Resolution,
    string Note,
    IReadOnlyList<ClosingCreditRef> Credits,
    decimal? ExpectedBalance) : IRequest<Result<CloseTerminatedAccountResult>>, IMoneyCriticalCommand
{
    public string AuditAction => AuditActions.TerminatedAccountClosed;
    public string AuditResourceType => ResourceTypes.Payee;
    public string? AuditResourceId => PayeeId.ToString();
    public string? AuditDisplayName => $"{Resolution} — {Currency}";

    /// <summary>
    /// ★★ THE IDS AND THE AMOUNTS, WHICH IS THE WHOLE POINT. The diagnosis found that a closure would
    /// otherwise be recorded as a flag and a paragraph, leaving "what happened to Birgit's €3,869.34"
    /// answerable only by reading prose.
    ///
    /// ★ AND IT IS SAFE TO TAKE THESE FROM THE REQUEST, which is not usually true of audit data. The
    /// handler refuses the whole closure unless this set matches the outstanding credits EXACTLY — by
    /// id and by amount, in both directions — so by the time this row is written the payload has been
    /// proven to be what was closed, not merely what was asked for.
    /// </summary>
    public Dictionary<string, string>? AuditMetadata => new()
    {
        ["resolution"] = Resolution.ToString(),
        ["currency"] = Currency,
        ["creditsClosed"] = Credits.Count.ToString(CultureInfo.InvariantCulture),
        ["creditTotalClosed"] = Credits
            .Sum(c => c.Amount).ToString(CultureInfo.InvariantCulture),
        ["closedCredits"] = string.Join(
            ";", Credits.Select(c => $"{c.CreditId}:{c.Amount.ToString(CultureInfo.InvariantCulture)}")),
        ["balanceBefore"] = (ExpectedBalance ?? 0m).ToString(CultureInfo.InvariantCulture),
        // Zero by construction: a closure that left a balance behind would not have closed the account.
        ["balanceAfter"] = "0",
        ["note"] = Note,
    };
}

/// <summary>One credit as the screen showed it. The amount is part of the identity, not decoration.</summary>
public sealed record ClosingCreditRef(Guid CreditId, decimal Amount);

/// <summary>
/// How the account was resolved. Three outcomes, because the ledger already distinguishes three and a
/// closure that collapsed them would make its own audit trail unanswerable.
/// </summary>
public enum AccountClosureResolution
{
    /// <summary>
    /// Recovered or paid OUTSIDE Wasnie — payroll took the debt from the final paycheck, or paid the
    /// commission with it. Credits close as <see cref="CreditClosureReason.ExternalSettlement"/>; a debt
    /// clears with <see cref="LedgerTransactionType.ExternalSettlementCredit"/> and a liability with
    /// <see cref="LedgerTransactionType.FinalSettlementDebit"/>.
    /// </summary>
    SettledExternally = 0,

    /// <summary>
    /// The company absorbed it. Credits close as <see cref="CreditClosureReason.WrittenOff"/>; a debt
    /// clears with <see cref="LedgerTransactionType.WriteOffCredit"/>.
    ///
    /// ★ This is the one that destroys a claim. Everything about the ceremony in front of it exists
    /// because of this value.
    /// </summary>
    WrittenOff = 1,
}

/// <summary>What was closed, so the caller can say it back to the user rather than guess.</summary>
public sealed record CloseTerminatedAccountResult(
    int CreditsClosed,
    decimal CreditTotalClosed,
    decimal BalanceBefore,
    decimal BalanceAfter,
    string Currency);
