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
    DateOnly TransactionDate,
    int Quantity = 1,
    bool ProcessImmediately = true,
    // Optional human-readable label of the sale ("Contrato Acme 2026"). Descriptive only —
    // it never participates in duplicate detection, matching or calculation.
    string? Description = null) : IRequest<Result<TransactionDto>>, IMoneyCriticalCommand
{
    public string AuditAction => AuditActions.TransactionIngested;
    public string AuditResourceType => ResourceTypes.Transaction;
    public string? AuditResourceId { get; set; }
    public string? AuditDisplayName => ReferenceNumber;
}
