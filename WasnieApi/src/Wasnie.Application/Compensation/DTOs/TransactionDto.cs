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
    // The admin's explicit plan attribution, when one was required at ingest.
    Guid? SelectedPlanAssignmentId = null,
    // What was sold — Description says which sale, these say which product.
    string? ProductName = null,
    string? ProductSku = null,
    // Enrichment output — the resolved category a rule trigger can filter on. Null when unmapped.
    string? Category = null,
    string? PayeeName = null,
    string? PayeeEmployeeCode = null,
    string? CancelledBy = null,
    DateTimeOffset? CancelledAt = null,
    string? CancelledReason = null);
