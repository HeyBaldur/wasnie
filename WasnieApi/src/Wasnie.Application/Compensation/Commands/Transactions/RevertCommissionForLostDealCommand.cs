using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Transactions;

/// <summary>
/// Explicit admin action: revert a CALCULATED commission whose CRM deal was lost. Supersedes the
/// transaction's live credits (non-destructive) and cancels the transaction. Paid is rejected (clawback is
/// out of scope). Never automatic — always confirmed by an admin from the deal-lost alert.
/// </summary>
public sealed record RevertCommissionForLostDealCommand(Guid TransactionId)
    : IRequest<Result<TransactionDto>>, IAuditableCommand
{
    public string? AuditResourceId { get; set; }
    public string AuditAction => "deal_lost_commission_reverted";
    public string AuditResourceType => "CompensationTransaction";
    public string? AuditDisplayName => TransactionId.ToString();
}
