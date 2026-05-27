using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Extensions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Plans;

namespace Wasnie.Application.Compensation.Handlers.Plans;

public sealed class ListPlansHandler(IApplicationDbContext db, IAuthorizationService authorizationService)
    : IRequestHandler<ListPlansQuery, Result<PagedResult<PlanSummaryDto>>>
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase) { "name", "version", "effectivestart", "effectiveend" };

    public async Task<Result<PagedResult<PlanSummaryDto>>> Handle(ListPlansQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PlansRead, cancellationToken);
        var p = request.Pagination;
        var query = db.CompensationPlans.Include(x => x.Rules).AsQueryable();

        // Search
        if (!string.IsNullOrWhiteSpace(p.Search))
        {
            var q = p.Search.Trim().ToLower();
            query = query.Where(x => x.Name.ToLower().Contains(q));
        }

        // Filters
        if (!string.IsNullOrWhiteSpace(p.Status) &&
            Enum.TryParse<PlanStatus>(p.Status, ignoreCase: true, out var status))
            query = query.Where(x => x.Status == status);

        // Sort
        var sortBy = AllowedSortFields.Contains(p.SortBy ?? "") ? p.SortBy!.ToLower() : "name";
        var desc = string.Equals(p.SortOrder, "desc", StringComparison.OrdinalIgnoreCase);

        query = sortBy switch
        {
            "version" => desc ? query.OrderByDescending(x => x.Version) : query.OrderBy(x => x.Version),
            "effectivestart" => desc ? query.OrderByDescending(x => x.EffectivePeriod.Start) : query.OrderBy(x => x.EffectivePeriod.Start),
            "effectiveend" => desc ? query.OrderByDescending(x => x.EffectivePeriod.End) : query.OrderBy(x => x.EffectivePeriod.End),
            _ => desc ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name),
        };

        var paged = await query.ToPagedResultAsync(p.Page, p.PageSize, cancellationToken);

        return Result<PagedResult<PlanSummaryDto>>.Success(new PagedResult<PlanSummaryDto>
        {
            Items = paged.Items.Select(CompensationMapper.ToPlanSummaryDto).ToList(),
            TotalCount = paged.TotalCount,
            Page = paged.Page,
            PageSize = paged.PageSize,
        });
    }
}
