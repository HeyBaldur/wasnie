namespace Wasnie.Application.Compensation.DTOs;

public sealed record RuleDto(
    Guid Id,
    string Name,
    int SortOrder,
    bool IsActive,
    object Trigger,
    object Measurement,
    object RateTable,
    object? Modifier,
    object? Cap,
    object? Floor);
