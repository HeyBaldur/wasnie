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

public sealed class ListPayeesByPlanHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IPayeeAccessGuard payeeAccessGuard)
    : IRequestHandler<ListPayeesByPlanQuery, Result<PagedResult<PlanAssignmentDto>>>
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase) { "payeefullname", "effectivestart" };

    public async Task<Result<PagedResult<PlanAssignmentDto>>> Handle(
        ListPayeesByPlanQuery request,
        CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.AssignmentsRead, cancellationToken);
        var p = request.Pagination;

        // "Who else is on this plan" — a roster of colleagues, so it is filtered like the other lists.
        // For a rep it collapses to whether THEY are on the plan, which is all they ever needed.
        var visibility = await payeeAccessGuard.GetVisibilityAsync(cancellationToken);
        var visibleIds = visibility.IsUnrestricted ? null : visibility.PayeeIds.ToArray();

        var joined = db.PlanAssignments
            .Where(a => a.PlanId == request.PlanId)
            .Where(a => visibleIds == null || visibleIds.Contains(a.PayeeId))
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
            "payeefullname" => desc ? joined.OrderByDescending(x => x.Assignment.PayeeSnapshot.FullName) : joined.OrderBy(x => x.Assignment.PayeeSnapshot.FullName),
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
