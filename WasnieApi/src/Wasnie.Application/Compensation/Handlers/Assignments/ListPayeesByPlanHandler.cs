using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Application.Compensation.Queries.Assignments;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Application.Common.Extensions;

namespace Wasnie.Application.Compensation.Handlers.Assignments;

public sealed class ListPayeesByPlanHandler(IApplicationDbContext db)
    : IRequestHandler<ListPayeesByPlanQuery, Result<PagedResult<PlanAssignmentDto>>>
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase) { "payeefullname", "effectivestart" };

    public async Task<Result<PagedResult<PlanAssignmentDto>>> Handle(
        ListPayeesByPlanQuery request,
        CancellationToken cancellationToken)
    {
        var p = request.Pagination;
        var query = db.PlanAssignments
            .Where(a => a.PlanId == request.PlanId)
            .AsQueryable();

        // Filters
        if (!string.IsNullOrWhiteSpace(p.Status) &&
            Enum.TryParse<AssignmentStatus>(p.Status, ignoreCase: true, out var status))
            query = query.Where(x => x.Status == status);

        // Sort
        var sortBy = AllowedSortFields.Contains(p.SortBy ?? "") ? p.SortBy!.ToLower() : "effectivestart";
        var desc = string.Equals(p.SortOrder, "desc", StringComparison.OrdinalIgnoreCase);

        query = sortBy switch
        {
            "payeefullname" => desc ? query.OrderByDescending(x => x.PayeeSnapshot.FullName) : query.OrderBy(x => x.PayeeSnapshot.FullName),
            _               => desc ? query.OrderByDescending(x => x.EffectivePeriod.Start)  : query.OrderBy(x => x.EffectivePeriod.Start),
        };

        var paged = await query.ToPagedResultAsync(p.Page, p.PageSize, cancellationToken);

        return Result<PagedResult<PlanAssignmentDto>>.Success(new PagedResult<PlanAssignmentDto>
        {
            Items = paged.Items.Select(CompensationMapper.ToPlanAssignmentDto).ToList(),
            TotalCount = paged.TotalCount,
            Page = paged.Page,
            PageSize = paged.PageSize,
        });
    }
}
