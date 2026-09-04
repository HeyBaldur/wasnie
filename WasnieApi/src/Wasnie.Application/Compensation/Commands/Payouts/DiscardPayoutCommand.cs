using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Payouts;

/// <summary>
/// Close an Approved payout that can never be paid, because every credit it carries was already paid
/// by a different payout.
///
/// ★ THE CLIENT SENDS THE PAYOUT AND THE REASON, NOTHING ELSE. Whether the payout really is
/// unpayable is decided by the server against the credits — the same rule the mark-paid guard uses.
/// A client that could assert "this one is safe to discard" could retire a payout somebody is still
/// owed.
///
/// ★ AUDITABLE, NOT MONEY-CRITICAL. No money moves and no credit changes hands: the entry records
/// that a payable figure stopped being owed. After KAN-34 that audit row only appears if the command
/// actually succeeded.
/// </summary>
public sealed record DiscardPayoutCommand(Guid PayoutId, string Reason)
    : IRequest<Result<DiscardPayoutResult>>, IAuditableCommand
{
    public string AuditAction => AuditActions.PayoutDiscarded;
    public string AuditResourceType => ResourceTypes.Payout;
    public string? AuditResourceId => PayoutId.ToString();
    public string? AuditDisplayName => null;

    /// <summary>
    /// ★ THE REASON TRAVELS INTO THE AUDIT ROW TOO. The payout keeps it as the field an auditor reads
    /// on the record itself; the audit log keeps it beside the actor and the timestamp, so "who
    /// decided this and why" is answerable from either end.
    /// </summary>
    public Dictionary<string, string>? AuditMetadata => new() { ["reason"] = Reason };
}

/// <summary>What was closed, echoed back so the screen can name it in its confirmation.</summary>
public sealed record DiscardPayoutResult(
    Guid PayoutId,
    string PayeeName,
    decimal Amount,
    string Currency,
    int CreditsAlreadyPaidElsewhere);
