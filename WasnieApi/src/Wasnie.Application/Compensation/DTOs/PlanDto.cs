namespace Wasnie.Application.Compensation.DTOs;

public sealed record PlanDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string Description,
    int Version,
    string Status,
    DateOnly EffectiveStart,
    DateOnly EffectiveEnd,
    string Currency,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    IList<RuleDto> Rules,
    // Clawback policy. Null/null means this plan claws nothing back — the state every plan starts in.
    int? ClawbackMaturationDays = null,
    decimal? ClawbackCapPercent = null);
