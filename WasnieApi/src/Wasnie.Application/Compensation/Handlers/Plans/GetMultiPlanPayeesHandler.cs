using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;

namespace Wasnie.Application.Compensation.Handlers.Plans;

/// <summary>
/// Returns the payees of a plan that are ALSO assigned to another active plan.
/// Anti-Cartesian (repo pattern — see GetDashboardSummaryHandler): a bounded set of queries plus
/// in-memory grouping. There is deliberately NO per-payee query — N+1 is the explicit anti-goal.
/// </summary>
public sealed class GetMultiPlanPayeesHandler(IApplicationDbContext db, IAuthorizationService authorizationService)
    : IRequestHandler<GetMultiPlanPayeesQuery, Result<MultiPlanPayeesDto>>
{
    public async Task<Result<MultiPlanPayeesDto>> Handle(
        GetMultiPlanPayeesQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PlansRead, cancellationToken);

        // Q1: active plans (id → name). Archived plans are excluded here, so a payee whose "other"
        // assignment belongs to an archived plan is NOT counted (archiving deactivates assignments too).
        var activePlans = await db.CompensationPlans
            .Where(p => p.Status == PlanStatus.Active)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(cancellationToken);

        var activePlanNameById = activePlans.ToDictionary(p => p.Id, p => p.Name);
        if (!activePlanNameById.ContainsKey(request.PlanId))
            return Result<MultiPlanPayeesDto>.Success(new MultiPlanPayeesDto(0, []));

        var activePlanIds = activePlanNameById.Keys.ToList();

        // Q2: active assignments of active plans (payee id + plan id + snapshot for display).
        var rows = await db.PlanAssignments
            .Where(a => a.Status == AssignmentStatus.Active && activePlanIds.Contains(a.PlanId))
            .Select(a => new
            {
                a.PayeeId,
                a.PlanId,
                FullName = a.PayeeSnapshot.FullName,
                EmployeeCode = a.PayeeSnapshot.EmployeeCode,
            })
            .ToListAsync(cancellationToken);

        // In-memory grouping: payee → its distinct active plan ids (+ a display snapshot).
        var byPayee = rows
            .GroupBy(r => r.PayeeId)
            .Select(g => new
            {
                PayeeId = g.Key,
                PlanIds = g.Select(x => x.PlanId).Distinct().ToList(),
                Display = g.First(),
            })
            .ToList();

        // Payees of THIS plan that are also in another active plan (>1 distinct active plan).
        var items = byPayee
            .Where(p => p.PlanIds.Contains(request.PlanId) && p.PlanIds.Count > 1)
            .Select(p => new MultiPlanPayeeDto(
                PayeeId: p.PayeeId,
                FullName: p.Display.FullName,
                EmployeeCode: p.Display.EmployeeCode,
                OtherPlans: p.PlanIds
                    .Where(pid => pid != request.PlanId)
                    .Select(pid => new OtherActivePlanDto(pid, activePlanNameById[pid]))
                    .OrderBy(op => op.PlanName)
                    .ToList()))
            .OrderBy(i => i.FullName)
            .ToList();

        return Result<MultiPlanPayeesDto>.Success(new MultiPlanPayeesDto(items.Count, items));
    }
}
