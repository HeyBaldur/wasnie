namespace Wasnie.Application.Compensation.DTOs;

public sealed record OverlappingPayoutDto(
    Guid Id,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    string PlanName,
    decimal TotalCommissionAmount,
    string TotalCommissionCurrency);

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
    string? TransactionCurrency,
    // Calculation explanation — null if credit data unavailable
    LineCalculationDto? Calculation);

// ── Calculation explanation DTOs ──────────────────────────────────────────────

public sealed record LineCalculationDto(
    int PlanVersion,
    DateTimeOffset FrozenAt,
    RateTableDto RateTable,
    TriggerDto Trigger,
    IReadOnlyList<ModifierApplicationDto> Modifiers);

public sealed record RateTableDto(
    string Type,
    decimal? FlatRate,
    IReadOnlyList<RateTierDto>? Tiers,
    IReadOnlyList<AttainmentTierDto>? AttainmentTiers);

public sealed record RateTierDto(decimal From, decimal? To, decimal Rate);

public sealed record AttainmentTierDto(decimal AttainmentFrom, decimal? AttainmentTo, decimal Rate);

public sealed record TriggerDto(
    bool IsAlways,
    string LogicalOperator,
    IReadOnlyList<ConditionDto> Conditions);

public sealed record ConditionDto(string Field, string Operator, string Value);

public sealed record ModifierApplicationDto(
    string ModifierName,
    decimal FactorApplied,
    decimal AmountBefore,
    string AmountBeforeCurrency,
    decimal AmountAfter,
    string AmountAfterCurrency);
