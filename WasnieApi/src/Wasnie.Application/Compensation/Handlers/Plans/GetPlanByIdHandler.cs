using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Plans;

public sealed class GetPlanByIdHandler(IApplicationDbContext db)
    : IRequestHandler<GetPlanByIdQuery, Result<PlanDto>>
{
    public async Task<Result<PlanDto>> Handle(GetPlanByIdQuery request, CancellationToken cancellationToken)
    {
        var plan = await db.CompensationPlans
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);

        return plan is null
            ? Result<PlanDto>.Failure("Plan not found.")
            : Result<PlanDto>.Success(CompensationMapper.ToPlanDto(plan));
    }
}
