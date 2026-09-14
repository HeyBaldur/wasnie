using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Extensions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

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
            _ => desc ? query.OrderByDescending(x => x.Version) : query.OrderBy(x => x.Version),
        };

        var paged = await query.ToPagedResultAsync(p.Page, p.PageSize, cancellationToken);

        var draftIds = paged.Items
            .Where(x => x.Status == Wasnie.Domain.Compensation.Plans.PlanStatus.Draft).Select(x => x.Id).ToList();
        var blockers = await PlanDeletionBlockers.FindAsync(db, draftIds, cancellationToken);

        return Result<PagedResult<PlanSummaryDto>>.Success(new PagedResult<PlanSummaryDto>
        {
            // ⚠ KNOWN DEFECT, KEPT AS IT WAS (reported with KAN-69, outside its scope): this used to be the
            // method group `Select(CompensationMapper.ToPlanSummaryDto)`, which binds to the (item, index)
            // overload of Select — so ActiveAssignmentCount has always been the ROW INDEX here, not a count.
            // Spelled out only because the mapper gained a parameter; fixing it is its own ticket.
            Items = paged.Items.Select((plan, index) => CompensationMapper.ToPlanSummaryDto(
                plan,
                index,
                isDeletable: plan.Status == Wasnie.Domain.Compensation.Plans.PlanStatus.Draft
                    && !blockers.ContainsKey(plan.Id))).ToList(),
            TotalCount = paged.TotalCount,
            Page = paged.Page,
            PageSize = paged.PageSize,
        });
    }
}
