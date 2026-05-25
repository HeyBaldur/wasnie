using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Application.Compensation.Queries.Assignments;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Assignments;

public sealed class ListAssignmentsByPayeeHandler(IApplicationDbContext db)
    : IRequestHandler<ListAssignmentsByPayeeQuery, Result<IList<PlanAssignmentDto>>>
{
    public async Task<Result<IList<PlanAssignmentDto>>> Handle(
        ListAssignmentsByPayeeQuery request,
        CancellationToken cancellationToken)
    {
        var assignments = await db.PlanAssignments
            .Where(a => a.PayeeId == request.PayeeId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        return Result<IList<PlanAssignmentDto>>.Success(
            assignments.Select(CompensationMapper.ToPlanAssignmentDto).ToList());
    }
}
