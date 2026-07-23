namespace Wasnie.Application.Compensation.DTOs;

/// <summary>
/// Payees of a given plan that are ALSO assigned to at least one other active plan.
/// Informational only — a transaction is credited to a single plan (see the plan-screen banner).
/// </summary>
public sealed record MultiPlanPayeesDto(int Count, IReadOnlyList<MultiPlanPayeeDto> Items);

public sealed record MultiPlanPayeeDto(
    Guid PayeeId,
    string FullName,
    string EmployeeCode,
    IReadOnlyList<OtherActivePlanDto> OtherPlans);

public sealed record OtherActivePlanDto(Guid PlanId, string PlanName);
