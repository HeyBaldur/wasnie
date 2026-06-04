namespace Wasnie.Application.Compensation.DTOs;

/// <summary>
/// Flat projection used exclusively for the Credits Excel export (WI-PROD-CREDITS-EXPORT).
/// </summary>
public sealed record CreditExportRow(
    Guid Id,
    string ReferenceNumber,
    string? PayeeName,
    string? PayeeCode,
    string PlanName,
    string RuleName,
    decimal OriginalAmount,
    string OriginalCurrency,
    decimal CreditedAmount,
    string CreditedCurrency,
    decimal SplitPercentage,
    string Role,
    DateTimeOffset AllocatedAt,
    string AllocatedBy,
    string Status,
    DateTimeOffset? SupersededAt,
    string? SupersededBy);
