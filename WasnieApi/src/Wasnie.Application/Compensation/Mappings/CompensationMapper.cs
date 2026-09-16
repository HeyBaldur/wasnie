using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Compensation.Assignments;
using CompensationPlan = Wasnie.Domain.Compensation.Plans.Plan;
using CompensationRule = Wasnie.Domain.Compensation.Plans.Rule;

namespace Wasnie.Application.Compensation.Mappings;

public static class CompensationMapper
{
    public static PlanDto ToPlanDto(CompensationPlan plan, int activeAssignmentCount) =>
        new(
            plan.Id,
            plan.TenantId,
            plan.Name,
            plan.Description,
            plan.Version,
            plan.Status.ToString(),
            plan.EffectivePeriod.Start,
            plan.EffectivePeriod.End,
            plan.Currency,
            plan.CreatedAt,
            plan.CreatedBy,
            plan.Rules.Select(ToRuleDto).ToList(),
            activeAssignmentCount,
            plan.ClawbackMaturationDays,
            plan.ClawbackCapPercent);

    public static PlanSummaryDto ToPlanSummaryDto(CompensationPlan plan, int activeAssignmentCount, bool isDeletable) =>
        new(
            plan.Id,
            plan.Name,
            plan.Version,
            plan.Status.ToString(),
            plan.EffectivePeriod.Start,
            plan.EffectivePeriod.End,
            plan.Currency,
            plan.Rules.Count(r => r.IsActive),
            activeAssignmentCount,
            isDeletable);

    public static RuleDto ToRuleDto(CompensationRule rule) =>
        new(
            rule.Id,
            rule.Name,
            rule.SortOrder,
            rule.IsActive,
            rule.Trigger,
            rule.Measurement,
            rule.RateTable,
            rule.Modifier,
            rule.Cap,
            rule.Floor,
            rule.StoppedAt,
            rule.StoppedBy,
            rule.StopReason);

    /// <param name="payeeFullName">The payee's CURRENT name — see ListAssignmentsHandler for why not the snapshot.</param>
    public static PlanAssignmentDto ToPlanAssignmentDto(
        PlanAssignment assignment, string planName, int planVersion, string payeeFullName, string payeeEmployeeCode) =>
        new(
            assignment.Id,
            assignment.TenantId,
            assignment.PlanId,
            planName,
            planVersion,
            assignment.PayeeId,
            payeeFullName,
            payeeEmployeeCode,
            assignment.EffectivePeriod.Start,
            assignment.EffectivePeriod.End,
            assignment.Status.ToString(),
            assignment.Notes,
            assignment.CreatedAt);
}
