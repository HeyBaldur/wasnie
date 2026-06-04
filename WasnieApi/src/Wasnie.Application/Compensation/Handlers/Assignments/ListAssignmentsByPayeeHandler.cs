using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Application.Compensation.Queries.Assignments;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Assignments;

public sealed class ListAssignmentsByPayeeHandler(IApplicationDbContext db, IAuthorizationService authorizationService, IClock clock)
    : IRequestHandler<ListAssignmentsByPayeeQuery, Result<PagedResult<PlanAssignmentDto>>>
{
    private sealed record AssignmentRow(Wasnie.Domain.Compensation.Assignments.PlanAssignment Assignment, string PlanName, int PlanVersion);

    public async Task<Result<PagedResult<PlanAssignmentDto>>> Handle(
        ListAssignmentsByPayeeQuery request,
        CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.AssignmentsRead, cancellationToken);
        var p = request.Pagination;

        // Load all assignments for this payee with plan info in-memory.
        // DateOnly period filtering on owned DateRange is unreliable in SQL WHERE.
        var allJoined = await (
            from a in db.PlanAssignments.Where(a => a.PayeeId == request.PayeeId)
            join pl in db.CompensationPlans on a.PlanId equals pl.Id
            select new { Assignment = a, PlanName = pl.Name, PlanVersion = pl.Version }
        ).ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(clock.UtcNow);

        // Apply status filter
        var filtered = allJoined.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(p.Status) &&
            Enum.TryParse<AssignmentStatus>(p.Status, ignoreCase: true, out var status))
            filtered = filtered.Where(x => x.Assignment.Status == status);

        // Apply period filter: "active" = EffectiveEnd >= today
        if (string.Equals(p.Period, "active", StringComparison.OrdinalIgnoreCase))
            filtered = filtered.Where(x => x.Assignment.EffectivePeriod.End >= today);

        // Sort
        var desc = !string.Equals(p.SortOrder, "asc", StringComparison.OrdinalIgnoreCase);
        var sortedList = desc
            ? filtered.OrderByDescending(x => x.Assignment.EffectivePeriod.Start).ToList()
            : filtered.OrderBy(x => x.Assignment.EffectivePeriod.Start).ToList();

        var totalCount = sortedList.Count;
        var pageItems = sortedList
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .ToList();

        var dtos = pageItems
            .Select(x => CompensationMapper.ToPlanAssignmentDto(x.Assignment, x.PlanName, x.PlanVersion))
            .ToList();

        return Result<PagedResult<PlanAssignmentDto>>.Success(new PagedResult<PlanAssignmentDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = p.Page,
            PageSize = p.PageSize,
        });
    }
}
