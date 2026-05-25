using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Plans;

namespace Wasnie.Application.Compensation.Handlers.Plans;

public sealed class ListPlansHandler(IApplicationDbContext db)
    : IRequestHandler<ListPlansQuery, Result<IList<PlanSummaryDto>>>
{
    public async Task<Result<IList<PlanSummaryDto>>> Handle(ListPlansQuery request, CancellationToken cancellationToken)
    {
        var query = db.CompensationPlans.Include(p => p.Rules).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.StatusFilter) &&
            Enum.TryParse<PlanStatus>(request.StatusFilter, ignoreCase: true, out var status))
        {
            query = query.Where(p => p.Status == status);
        }

        var plans = await query
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return Result<IList<PlanSummaryDto>>.Success(
            plans.Select(CompensationMapper.ToPlanSummaryDto).ToList());
    }
}
