using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Transactions;

public sealed record VoidTransactionCommand(Guid TransactionId, string Reason)
    : IRequest<Result<TransactionDto>>, IAuditableCommand
{
    public string? AuditResourceId { get; set; }
    public string AuditAction => "transaction_voided";
    public string AuditResourceType => "CompensationTransaction";
    public string? AuditDisplayName => TransactionId.ToString();
}
