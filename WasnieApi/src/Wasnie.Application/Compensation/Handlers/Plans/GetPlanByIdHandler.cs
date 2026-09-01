using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Plans;

public sealed class GetPlanByIdHandler(IApplicationDbContext db, IAuthorizationService authorizationService)
    : IRequestHandler<GetPlanByIdQuery, Result<PlanDto>>
{
    public async Task<Result<PlanDto>> Handle(GetPlanByIdQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PlansRead, cancellationToken);
        // Only return active rules: deleting a rule soft-deactivates it (IsActive=false),
        // and returning deactivated rules makes them reappear in the UI where editing them
        // fails with "rule not found in this plan" (UpdateRule filters on IsActive).
        // Order by SortOrder server-side; ThenBy(Id) is a stable tie-break for colliding orders.
        var plan = await db.CompensationPlans
            .Include(p => p.Rules.Where(r => r.IsActive).OrderBy(r => r.SortOrder).ThenBy(r => r.Id))
            .FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);

        if (plan is null)
        {
            return Result<PlanDto>.Failure("Plan not found.");
        }

        // Counted here rather than derived from a navigation: PlanAssignment is a separate aggregate.
        // The archive confirmation shows this number, so it has to be the same predicate
        // ArchivePlanHandler deactivates on — Active assignments of this plan, nothing else.
        var activeAssignmentCount = await db.PlanAssignments
            .CountAsync(a => a.PlanId == plan.Id && a.Status == AssignmentStatus.Active, cancellationToken);

        return Result<PlanDto>.Success(CompensationMapper.ToPlanDto(plan, activeAssignmentCount));
    }
}
