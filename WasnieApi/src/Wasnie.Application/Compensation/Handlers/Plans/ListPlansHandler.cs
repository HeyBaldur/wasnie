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
        new(StringComparer.OrdinalIgnoreCase) { "name", "version", "effectivestart", "effectiveend", "createdat" };

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

        // Status filters
        // Singular ?status=Active takes precedence for backward compat.
        // Plural ?statuses=Active,Archived supports multi-status (e.g. exclude Draft from lookup dropdowns).
        if (!string.IsNullOrWhiteSpace(p.Status) &&
            Enum.TryParse<PlanStatus>(p.Status, ignoreCase: true, out var status))
        {
            query = query.Where(x => x.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(p.Statuses))
        {
            var statuses = p.Statuses
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Enum.TryParse<PlanStatus>(s.Trim(), ignoreCase: true, out var st) ? (PlanStatus?)st : null)
                .Where(s => s.HasValue)
                .Select(s => s!.Value)
                .ToHashSet();
            if (statuses.Count > 0)
                query = query.Where(x => statuses.Contains(x.Status));
        }

        // Sort
        var sortBy = AllowedSortFields.Contains(p.SortBy ?? "") ? p.SortBy!.ToLower() : "name";
        var desc = string.Equals(p.SortOrder, "desc", StringComparison.OrdinalIgnoreCase);

        query = sortBy switch
        {
            "version" => desc ? query.OrderByDescending(x => x.Version) : query.OrderBy(x => x.Version),
            "effectivestart" => desc ? query.OrderByDescending(x => x.EffectivePeriod.Start) : query.OrderBy(x => x.EffectivePeriod.Start),
            "effectiveend" => desc ? query.OrderByDescending(x => x.EffectivePeriod.End) : query.OrderBy(x => x.EffectivePeriod.End),
            // Id is a stable tiebreak so pagination stays deterministic when two plans share a timestamp.
            "createdat" => desc
                ? query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                : query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
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
