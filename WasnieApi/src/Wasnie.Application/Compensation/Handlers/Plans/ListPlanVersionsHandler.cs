using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Plans;

public sealed class ListPlanVersionsHandler(IApplicationDbContext db)
    : IRequestHandler<ListPlanVersionsQuery, Result<IList<PlanSummaryDto>>>
{
    public async Task<Result<IList<PlanSummaryDto>>> Handle(ListPlanVersionsQuery request, CancellationToken cancellationToken)
    {
        var plans = await db.CompensationPlans
            .Include(p => p.Rules)
            .Where(p => p.Name == request.PlanName)
            .OrderBy(p => p.Version)
            .ToListAsync(cancellationToken);

        return Result<IList<PlanSummaryDto>>.Success(
            plans.Select(CompensationMapper.ToPlanSummaryDto).ToList());
    }
}
