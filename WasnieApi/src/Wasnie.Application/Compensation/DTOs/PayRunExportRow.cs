namespace Wasnie.Application.Compensation.DTOs;

public sealed record PayRunExportRow(
    Guid Id,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    int PayeeCount,
    int PaidPayeeCount,
    int ZeroPayoutCount,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    string? PaidBy,
    DateTimeOffset? PaidAt,
    IReadOnlyDictionary<string, decimal> TotalAmounts);
