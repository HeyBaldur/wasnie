namespace Wasnie.Application.Compensation.DTOs;

/// <summary>
/// Plan attribution options for a manual transaction.
/// <paramref name="SelectionRequired"/> is computed server-side (2+ options) so the form and the
/// ingest validation agree on when a choice is mandatory instead of each deciding for itself.
/// </summary>
public sealed record PlanOptionsDto(
    IReadOnlyList<PlanOptionDto> Options,
    bool SelectionRequired);

/// <summary>
/// One selectable attribution. The identifier is the ASSIGNMENT, not the plan: a payee can hold two
/// assignments to the same plan over different periods, and only the assignment is unambiguous —
/// selecting by plan id would hand the tie-break back to the engine, which is the bug being fixed.
/// </summary>
public sealed record PlanOptionDto(
    Guid PlanAssignmentId,
    Guid PlanId,
    string PlanName,
    string PlanCurrency,
    DateOnly EffectiveStart,
    DateOnly EffectiveEnd);
