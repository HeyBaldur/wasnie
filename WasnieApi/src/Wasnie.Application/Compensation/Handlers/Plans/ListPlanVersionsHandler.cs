using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Application.Common.Extensions;

namespace Wasnie.Application.Compensation.Handlers.Plans;

public sealed class ListPlanVersionsHandler(IApplicationDbContext db, IAuthorizationService authorizationService)
    : IRequestHandler<ListPlanVersionsQuery, Result<PagedResult<PlanSummaryDto>>>
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase) { "version", "effectivestart" };

    public async Task<Result<PagedResult<PlanSummaryDto>>> Handle(ListPlanVersionsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PlansRead, cancellationToken);
        var p = request.Pagination;
        var query = db.CompensationPlans
            .Include(x => x.Rules)
            .Where(x => x.Name == request.PlanName)
            .AsQueryable();

        // Filters
        if (!string.IsNullOrWhiteSpace(p.Status) &&
            Enum.TryParse<Wasnie.Domain.Compensation.Plans.PlanStatus>(p.Status, ignoreCase: true, out var status))
            query = query.Where(x => x.Status == status);

        // Sort
        var sortBy = AllowedSortFields.Contains(p.SortBy ?? "") ? p.SortBy!.ToLower() : "version";
        var desc = string.Equals(p.SortOrder, "desc", StringComparison.OrdinalIgnoreCase);

        query = sortBy switch
        {
            "effectivestart" => desc ? query.OrderByDescending(x => x.EffectivePeriod.Start) : query.OrderBy(x => x.EffectivePeriod.Start),
            _                => desc ? query.OrderByDescending(x => x.Version)               : query.OrderBy(x => x.Version),
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
