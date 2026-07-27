using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Assignments;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Assignments;

public sealed class ListAssignmentsHandler(IApplicationDbContext db, IAuthorizationService authorizationService)
    : IRequestHandler<ListAssignmentsQuery, Result<PagedResult<PlanAssignmentSummaryDto>>>
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase) { "payeefullname", "effectivestart", "planname" };

    public async Task<Result<PagedResult<PlanAssignmentSummaryDto>>> Handle(
        ListAssignmentsQuery request,
        CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.AssignmentsRead, cancellationToken);
        var p = request.Pagination;
        var query = db.PlanAssignments.AsQueryable();

        // Filters
        if (!string.IsNullOrWhiteSpace(p.Status) &&
            Enum.TryParse<AssignmentStatus>(p.Status, ignoreCase: true, out var status))
            query = query.Where(x => x.Status == status);

        // Exact payee filter — used by the "View all" deep-link from a payee's Assignments card, so the
        // user lands on that payee's assignments instead of the full list. Matches on Id rather than
        // reusing Search (which is a substring match on name/code and could pull in a similar code).
        if (p.PayeeId.HasValue)
            query = query.Where(x => x.PayeeId == p.PayeeId.Value);

        // Join with plans for planName sorting
        var joined = query.Join(
            db.CompensationPlans,
            a => a.PlanId,
            pl => pl.Id,
            (a, pl) => new
            {
                Assignment = a,
                PlanName = pl.Name,
                PlanVersion = pl.Version,
            });

        // Search
        if (!string.IsNullOrWhiteSpace(p.Search))
        {
            var srch = p.Search.Trim().ToLower();
            joined = joined.Where(x =>
                x.Assignment.PayeeSnapshot.FullName.ToLower().Contains(srch) ||
                x.Assignment.PayeeSnapshot.EmployeeCode.ToLower().Contains(srch));
        }

        // Sort
        var sortBy = AllowedSortFields.Contains(p.SortBy ?? "") ? p.SortBy!.ToLower() : "effectivestart";
        var desc = string.Equals(p.SortOrder, "desc", StringComparison.OrdinalIgnoreCase);

        var sorted = sortBy switch
        {
            "payeefullname" => desc ? joined.OrderByDescending(x => x.Assignment.PayeeSnapshot.FullName) : joined.OrderBy(x => x.Assignment.PayeeSnapshot.FullName),
            "planname" => desc ? joined.OrderByDescending(x => x.PlanName) : joined.OrderBy(x => x.PlanName),
            _ => desc ? joined.OrderByDescending(x => x.Assignment.EffectivePeriod.Start) : joined.OrderBy(x => x.Assignment.EffectivePeriod.Start),
        };

        var totalCount = await sorted.CountAsync(cancellationToken);
        var items = await sorted
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(x => new PlanAssignmentSummaryDto(
            x.Assignment.Id,
            x.Assignment.TenantId,
            x.Assignment.PlanId,
            x.PlanName,
            x.PlanVersion,
            x.Assignment.PayeeId,
            x.Assignment.PayeeSnapshot.FullName,
            x.Assignment.PayeeSnapshot.EmployeeCode,
            x.Assignment.EffectivePeriod.Start,
            x.Assignment.EffectivePeriod.End,
            x.Assignment.Status.ToString(),
            x.Assignment.Notes,
            x.Assignment.CreatedAt)).ToList();

        return Result<PagedResult<PlanAssignmentSummaryDto>>.Success(new PagedResult<PlanAssignmentSummaryDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = p.Page,
            PageSize = p.PageSize,
        });
    }
}
