using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Transactions;

public sealed record ReassignPayeeCommand(
    Guid TransactionId,
    Guid NewPayeeId,
    string Reason) : IRequest<Result<TransactionDto>>, IMoneyCriticalCommand
{
    public string AuditAction => AuditActions.TransactionPayeeReassigned;
    public string AuditResourceType => ResourceTypes.Transaction;
    public string? AuditResourceId { get; set; }
    public string? AuditDisplayName => TransactionId.ToString();
}
