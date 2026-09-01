namespace Wasnie.Application.Compensation.DTOs;

public sealed record PlanSummaryDto(
    Guid Id,
    string Name,
    int Version,
    string Status,
    DateOnly EffectiveStart,
    DateOnly EffectiveEnd,
    string Currency,
    int ActiveRuleCount,
    // Active assignments. The list screen archives plans too, and its confirmation must name the
    // same number the detail screen does.
    int ActiveAssignmentCount);
