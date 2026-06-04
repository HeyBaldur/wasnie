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
    string? PayeeName = null,
    string? PayeeEmployeeCode = null);
