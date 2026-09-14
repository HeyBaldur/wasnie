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

public sealed class ListAssignmentsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IPayeeAccessGuard payeeAccessGuard)
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

        // Filtered, not refused — the tenant-wide assignment list is a legitimate screen for finance,
        // and for a rep it collapses to their own row.
        var visibility = await payeeAccessGuard.GetVisibilityAsync(cancellationToken);
        var visibleIds = visibility.IsUnrestricted ? null : visibility.PayeeIds.ToArray();
        if (visibleIds is not null)
            query = query.Where(x => visibleIds.Contains(x.PayeeId));

        // Filters
        if (!string.IsNullOrWhiteSpace(p.Status) &&
            Enum.TryParse<AssignmentStatus>(p.Status, ignoreCase: true, out var status))
            query = query.Where(x => x.Status == status);

        // Exact payee filter — used by the "View all" deep-link from a payee's Assignments card, so the
        // user lands on that payee's assignments instead of the full list. Matches on Id rather than
        // reusing Search (which is a substring match on name/code and could pull in a similar code).
        if (p.PayeeId.HasValue)
            query = query.Where(x => x.PayeeId == p.PayeeId.Value);

        // Join with plans for planName sorting, and LEFT join with payees for the current name.
        // ★ THE PAYEE'S NAME IS READ LIVE, NOT FROM THE ASSIGNMENT'S SNAPSHOT. PayeeSnapshot is the name as it was
        // when the assignment was created, and it never changes. Showing it made one renamed payee look like
        // two people with the same code (CEO-001: "Rudolph" on the 9 rows created before the rename,
        // "Rudolph GeHard Chipellin 3ero" on the 4 after). An assignment is a live relationship, so it shows
        // who the payee IS. The snapshot stays in the row untouched (§B6) and is only the fallback when the
        // payee row cannot be read.
        var joined =
            from a in query
            join pl in db.CompensationPlans on a.PlanId equals pl.Id
            join py in db.Payees on a.PayeeId equals py.Id into payees
            from py in payees.DefaultIfEmpty()
            select new
            {
                Assignment = a,
                PlanName = pl.Name,
                PlanVersion = pl.Version,
                PayeeFullName = py != null ? py.FullName : a.PayeeSnapshot.FullName,
                PayeeEmployeeCode = py != null ? py.EmployeeCode : a.PayeeSnapshot.EmployeeCode,
            };

        // Search
        if (!string.IsNullOrWhiteSpace(p.Search))
        {
            var srch = p.Search.Trim().ToLower();
            // The current name AND the name at assignment time both match: someone who still knows the payee
            // by the old name must not get an empty list.
            joined = joined.Where(x =>
                x.PayeeFullName.ToLower().Contains(srch) ||
                x.PayeeEmployeeCode.ToLower().Contains(srch) ||
                x.Assignment.PayeeSnapshot.FullName.ToLower().Contains(srch));
        }

        // Sort
        var sortBy = AllowedSortFields.Contains(p.SortBy ?? "") ? p.SortBy!.ToLower() : "effectivestart";
        var desc = string.Equals(p.SortOrder, "desc", StringComparison.OrdinalIgnoreCase);

        var sorted = sortBy switch
        {
            "payeefullname" => desc ? joined.OrderByDescending(x => x.PayeeFullName) : joined.OrderBy(x => x.PayeeFullName),
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
            x.PayeeFullName,
            x.PayeeEmployeeCode,
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
