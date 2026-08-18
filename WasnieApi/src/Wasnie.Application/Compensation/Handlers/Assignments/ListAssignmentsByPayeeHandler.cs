using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Helpers;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Application.Compensation.Queries.Assignments;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Assignments;

/// <summary>
/// A payee's plan assignments.
///
/// CONTRACT — the caller says what it wants; this handler never guesses:
///
///   status   Active (default) | Deactivated | all
///            A STATUS filter, not a date one. Defaulting to Active is safe because it cannot
///            hide a current assignment — only deactivated ones, and surfacing those has to be
///            asked for (a payee card used to count deactivated rows as if they were current).
///            Anything unrecognised falls back to Active: the permissive outcome is never the
///            result of a typo.
///
///   dateFrom / dateTo   explicit range; an assignment matches when its effective period
///                       INTERSECTS it. Either bound may be omitted.
///
///   period   the named-range convention (this-month, last-month, ytd, all…), honoured ONLY
///            when the caller actually sends it.
///
///   none of the above → NO date filter. The full history is returned.
///
/// That last line is the change. This endpoint used to resolve a missing `period` to
/// "this-month", so opening a payee's profile silently dropped every assignment that had
/// expired — the caller never asked for that and was never told. Which slice of history to
/// show is a presentation decision and belongs to the client.
/// </summary>
public sealed class ListAssignmentsByPayeeHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IPayeeAccessGuard payeeAccessGuard,
    IClock clock)
    : IRequestHandler<ListAssignmentsByPayeeQuery, Result<PagedResult<PlanAssignmentDto>>>
{
    /// <summary>Explicit opt-in to return every status.</summary>
    public const string AllStatuses = "all";

    public async Task<Result<PagedResult<PlanAssignmentDto>>> Handle(
        ListAssignmentsByPayeeQuery request,
        CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.AssignmentsRead, cancellationToken);
        var p = request.Pagination;

        // Which plan a colleague is on, on what terms and since when. No amount in the DTO, but it is
        // the STRUCTURE of someone else's compensation, and it is one join away from their rates.
        if (!await payeeAccessGuard.CanReadAsync(request.PayeeId, cancellationToken))
            return Result<PagedResult<PlanAssignmentDto>>.Success(PagedResult<PlanAssignmentDto>.Empty(p.Page, p.PageSize));

        // Everything below composes onto ONE IQueryable that is materialised exactly once, at the
        // end, already filtered, sorted and paged. It used to load every assignment of the payee and
        // discard in memory: a rep with 500 historical rows and 2 active ones shipped 498 rows across
        // the wire per request, and TotalCount was computed on a list the database had already sent.
        var query =
            from a in db.PlanAssignments.Where(a => a.PayeeId == request.PayeeId)
            join pl in db.CompensationPlans on a.PlanId equals pl.Id
            select new { Assignment = a, PlanName = pl.Name, PlanVersion = pl.Version };

        // ── Status ───────────────────────────────────────────────────────────
        if (string.Equals(p.Status, AllStatuses, StringComparison.OrdinalIgnoreCase))
        {
            // Explicit opt-in: no status filter at all.
        }
        else if (!string.IsNullOrWhiteSpace(p.Status) &&
                 Enum.TryParse<AssignmentStatus>(p.Status, ignoreCase: true, out var status))
        {
            query = query.Where(x => x.Assignment.Status == status);
        }
        else
        {
            query = query.Where(x => x.Assignment.Status == AssignmentStatus.Active);
        }

        // ── Date range: explicit only ────────────────────────────────────────
        var (from, to) = ResolveExplicitRange(p);

        if (from.HasValue)
        {
            var fromValue = from.Value;
            query = query.Where(x => x.Assignment.EffectivePeriod.End >= fromValue);
        }

        if (to.HasValue)
        {
            var toValue = to.Value;
            query = query.Where(x => x.Assignment.EffectivePeriod.Start <= toValue);
        }

        // ── Sort + page, both in SQL ─────────────────────────────────────────
        var desc = !string.Equals(p.SortOrder, "asc", StringComparison.OrdinalIgnoreCase);
        query = desc
            ? query.OrderByDescending(x => x.Assignment.EffectivePeriod.Start)
            : query.OrderBy(x => x.Assignment.EffectivePeriod.Start);

        var totalCount = await query.CountAsync(cancellationToken);

        var pageItems = await query
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .ToListAsync(cancellationToken);

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

    /// <summary>
    /// The date window the caller ASKED for, or (null, null) when it asked for none.
    /// PeriodHelper is only consulted when `period` was actually sent — calling it with a null
    /// period is what used to manufacture the hidden "this-month" default. PeriodHelper itself is
    /// shared with other endpoints and is deliberately left untouched.
    /// </summary>
    private (DateOnly? From, DateOnly? To) ResolveExplicitRange(PaginationQuery p)
    {
        if (p.DateFrom.HasValue || p.DateTo.HasValue)
            return (p.DateFrom, p.DateTo);

        if (!string.IsNullOrWhiteSpace(p.Period))
            return PeriodHelper.ComputeDateRange(p.Period, DateOnly.FromDateTime(clock.UtcNow));

        return (null, null);
    }
}
