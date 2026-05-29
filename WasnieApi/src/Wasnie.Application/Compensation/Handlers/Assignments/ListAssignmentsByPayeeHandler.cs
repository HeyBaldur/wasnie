using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Application.Compensation.Queries.Assignments;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Assignments;

public sealed class ListAssignmentsByPayeeHandler(IApplicationDbContext db, IAuthorizationService authorizationService)
    : IRequestHandler<ListAssignmentsByPayeeQuery, Result<PagedResult<PlanAssignmentDto>>>
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase) { "effectivestart", "planname" };

    public async Task<Result<PagedResult<PlanAssignmentDto>>> Handle(
        ListAssignmentsByPayeeQuery request,
        CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.AssignmentsRead, cancellationToken);
        var p = request.Pagination;

        var joined = db.PlanAssignments
            .Where(a => a.PayeeId == request.PayeeId)
            .Join(
                db.CompensationPlans,
                a => a.PlanId,
                pl => pl.Id,
                (a, pl) => new { Assignment = a, PlanName = pl.Name, PlanVersion = pl.Version });

        // Filters
        if (!string.IsNullOrWhiteSpace(p.Status) &&
            Enum.TryParse<AssignmentStatus>(p.Status, ignoreCase: true, out var status))
            joined = joined.Where(x => x.Assignment.Status == status);

        // Sort
        var sortBy = AllowedSortFields.Contains(p.SortBy ?? "") ? p.SortBy!.ToLower() : "effectivestart";
        var desc = string.Equals(p.SortOrder, "desc", StringComparison.OrdinalIgnoreCase);

        var sorted = sortBy switch
        {
            "planname" => desc ? joined.OrderByDescending(x => x.PlanName) : joined.OrderBy(x => x.PlanName),
            _ => desc ? joined.OrderByDescending(x => x.Assignment.EffectivePeriod.Start) : joined.OrderBy(x => x.Assignment.EffectivePeriod.Start),
        };

        var totalCount = await sorted.CountAsync(cancellationToken);
        var items = await sorted
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(x => CompensationMapper.ToPlanAssignmentDto(x.Assignment, x.PlanName, x.PlanVersion)).ToList();

        return Result<PagedResult<PlanAssignmentDto>>.Success(new PagedResult<PlanAssignmentDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = p.Page,
            PageSize = p.PageSize,
        });
    }
}
