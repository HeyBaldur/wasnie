namespace Wasnie.Application.Compensation.DTOs;

public sealed record PayoutExportRow(
    Guid Id,
    string PayeeName,
    string? PayeeCode,
    string PlanName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TotalCommissionAmount,
    string TotalCommissionCurrency,
    string Status,
    DateTimeOffset CalculatedAt,
    DateTimeOffset UpdatedAt);
