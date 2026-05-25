using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Application.Compensation.Queries.Assignments;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Assignments;

public sealed class ListPayeesByPlanHandler(IApplicationDbContext db)
    : IRequestHandler<ListPayeesByPlanQuery, Result<IList<PlanAssignmentDto>>>
{
    public async Task<Result<IList<PlanAssignmentDto>>> Handle(
        ListPayeesByPlanQuery request,
        CancellationToken cancellationToken)
    {
        var assignments = await db.PlanAssignments
            .Where(a => a.PlanId == request.PlanId)
            .OrderBy(a => a.PayeeSnapshot.FullName)
            .ToListAsync(cancellationToken);

        return Result<IList<PlanAssignmentDto>>.Success(
            assignments.Select(CompensationMapper.ToPlanAssignmentDto).ToList());
    }
}
