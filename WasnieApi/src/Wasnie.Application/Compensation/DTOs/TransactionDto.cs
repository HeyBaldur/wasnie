namespace Wasnie.Application.Compensation.DTOs;

public sealed record TransactionDto(
    Guid Id,
    Guid TenantId,
    string ReferenceNumber,
    Guid? PayeeId,
    decimal Amount,
    string Currency,
    int Quantity,
    DateOnly TransactionDate,
    string Source,
    string Status,
    string? ExternalId,
    DateTimeOffset IngestedAt,
    string IngestedBy,
    DateTimeOffset UpdatedAt,
    // Human-readable label of the sale (HubSpot deal name / manual / Excel). Display only.
    string? Description = null,
    string? PayeeName = null,
    string? PayeeEmployeeCode = null,
    string? CancelledBy = null,
    DateTimeOffset? CancelledAt = null,
    string? CancelledReason = null);
