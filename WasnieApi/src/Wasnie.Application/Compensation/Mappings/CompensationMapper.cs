using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Quotas;
using CompensationPlan = Wasnie.Domain.Compensation.Plans.Plan;
using CompensationRule = Wasnie.Domain.Compensation.Plans.Rule;

namespace Wasnie.Application.Compensation.Mappings;

public static class CompensationMapper
{
    public static PlanDto ToPlanDto(CompensationPlan plan) =>
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
            plan.Rules.Select(ToRuleDto).ToList());

    public static PlanSummaryDto ToPlanSummaryDto(CompensationPlan plan) =>
        new(
            plan.Id,
            plan.Name,
            plan.Version,
            plan.Status.ToString(),
            plan.EffectivePeriod.Start,
            plan.EffectivePeriod.End,
            plan.Currency,
            plan.Rules.Count(r => r.IsActive));

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
            rule.Floor);

    public static QuotaDto ToQuotaDto(Quota quota) =>
        new(
            quota.Id,
            quota.TenantId,
            quota.PayeeId,
            quota.PlanId,
            quota.Amount.Amount,
            quota.Amount.Currency,
            quota.Period.Start,
            quota.Period.End,
            quota.Status.ToString(),
            quota.CreatedAt);

    public static PlanAssignmentDto ToPlanAssignmentDto(PlanAssignment assignment) =>
        new(
            assignment.Id,
            assignment.TenantId,
            assignment.PlanId,
            assignment.PayeeId,
            assignment.PayeeSnapshot.FullName,
            assignment.PayeeSnapshot.EmployeeCode,
            assignment.EffectivePeriod.Start,
            assignment.EffectivePeriod.End,
            assignment.Status.ToString(),
            assignment.CreatedAt);
}
