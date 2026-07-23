using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

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

        return plan is null
            ? Result<PlanDto>.Failure("Plan not found.")
            : Result<PlanDto>.Success(CompensationMapper.ToPlanDto(plan));
    }
}
