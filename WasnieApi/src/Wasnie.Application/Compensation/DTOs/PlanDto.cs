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
    //
    // NO DEFAULT VALUE on purpose, even though both are nullable TYPES. They used to default to null,
    // and a mapper that simply stopped at Rules compiled cleanly while reporting "no clawback policy"
    // for every plan in the system — the policy was stored correctly and the screen showed it empty.
    // A defaulted parameter turns a forgotten field into a plausible lie; without the default, the same
    // omission is a compiler error. Nullability stays: a plan with no policy legitimately carries nulls.
    int? ClawbackMaturationDays,
    decimal? ClawbackCapPercent);
