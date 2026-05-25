namespace Wasnie.Application.Compensation.DTOs;

public sealed record PlanSummaryDto(
    Guid Id,
    string Name,
    int Version,
    string Status,
    DateOnly EffectiveStart,
    DateOnly EffectiveEnd,
    string Currency,
    int ActiveRuleCount);
