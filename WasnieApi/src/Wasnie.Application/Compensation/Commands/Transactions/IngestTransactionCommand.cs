using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Transactions;

public sealed record IngestTransactionCommand(
    string ReferenceNumber,
    Guid? PayeeId,
    decimal Amount,
    string Currency,
    DateOnly TransactionDate) : IRequest<Result<TransactionDto>>, IMoneyCriticalCommand
{
    public string AuditAction => AuditActions.TransactionIngested;
    public string AuditResourceType => ResourceTypes.Transaction;
    public string? AuditResourceId { get; set; }
    public string? AuditDisplayName => ReferenceNumber;
}
