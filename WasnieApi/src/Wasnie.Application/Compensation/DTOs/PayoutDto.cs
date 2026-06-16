namespace Wasnie.Application.Compensation.DTOs;

public sealed record PayoutListItemDto(
    Guid Id,
    Guid PayeeId,
    string PayeeName,
    string PayeeCode,
    Guid PlanId,
    string PlanName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TotalCommissionAmount,
    string TotalCommissionCurrency,
    string Status,
    DateTimeOffset CalculatedAt,
    string CalculatedBy,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);

public sealed record PayoutDto(
    Guid Id,
    Guid TenantId,
    Guid PayeeId,
    string PayeeName,
    string PayeeCode,
    Guid PlanId,
    string PlanName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TotalCommissionAmount,
    string TotalCommissionCurrency,
    string Status,
    DateTimeOffset CalculatedAt,
    string CalculatedBy,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    IReadOnlyList<PayoutLineDto> Lines);

public sealed record PayoutLineDto(
    Guid Id,
    Guid CreditId,
    Guid RuleId,
    string RuleName,
    decimal BaseAmount,
    string BaseCurrency,
    decimal CommissionAmount,
    string CommissionCurrency,
    // Source transaction — null only if data is missing (should not occur in normal operation)
    Guid? TransactionId,
    string? TransactionReference,
    string? TransactionExternalId,
    DateOnly? TransactionDate,
    decimal? TransactionAmount,
    string? TransactionCurrency);
