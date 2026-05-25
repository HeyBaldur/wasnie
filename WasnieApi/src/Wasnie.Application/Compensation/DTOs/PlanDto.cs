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
    IList<RuleDto> Rules);
