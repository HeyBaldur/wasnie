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
    string? Description = null,
    // REQUIRED when the payee has 2+ applicable plan assignments: the admin must state which plan
    // this sale belongs to instead of letting the engine tie-break. Ignored (and rejected as
    // unnecessary) when there is no ambiguity. See IngestTransactionHandler.
    Guid? SelectedPlanAssignmentId = null,
    // What was sold. Optional and descriptive — Description says which sale, these say which product.
    string? ProductName = null,
    string? ProductSku = null,
    // Optional explicit category. When present it WINS over the SKU/name resolver (the admin was
    // explicit); blank/null → the resolver still runs. Never required.
    string? Category = null) : IRequest<Result<TransactionDto>>, IMoneyCriticalCommand
{
    public string AuditAction => AuditActions.TransactionIngested;
    public string AuditResourceType => ResourceTypes.Transaction;
    public string? AuditResourceId { get; set; }
    public string? AuditDisplayName => ReferenceNumber;
}
