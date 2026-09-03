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
    object? Floor,

    // The three fields that say WHICH KIND of inactive this rule is. A client that sees IsActive
    // false and StoppedAt null is looking at a rule removed from a draft; one with StoppedAt set is
    // looking at a rule someone braked on a live plan, and the date and reason are what it shows.
    DateTimeOffset? StoppedAt = null,
    string? StoppedBy = null,
    string? StopReason = null);
