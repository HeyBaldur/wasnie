using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Audit.DTOs;
using Wasnie.Application.Audit.Queries;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Audit.Handlers;

/// <summary>
/// The tenant's audit trail, paged and filtered by the server (KAN-19).
///
/// ★★ THE FILTER IS APPLIED ONCE AND FEEDS BOTH THE COUNT AND THE PAGE. Counting with one predicate
/// and paging with another is how a table comes to say "247 entries" over eleven rows; the shared
/// <see cref="Filtered"/> makes the two impossible to separate.
///
/// ★ TENANT SCOPING IS THE GLOBAL QUERY FILTER'S JOB (Rule 9), not this handler's. Nothing here
/// mentions TenantId — the moment a query in this file did, it would be the one place that could
/// disagree with every other read in the app.
/// </summary>
public sealed class GetAuditLogsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetAuditLogsQuery, Result<AuditLogPageDto>>
{
    private const int MaxPageSize = 200;

    /// <summary>
    /// The value the client sends to mean "the rows no human performed".
    ///
    /// ★ NEVER STORED, ONLY MATCHED. It is not a valid email address, so it cannot collide with a
    /// real actor. The front declares the same word in `SYSTEM_ACTOR`.
    /// </summary>
    internal const string SystemActor = "__system__";

    public async Task<Result<AuditLogPageDto>> Handle(GetAuditLogsQuery request, CancellationToken ct)
    {
        await authorizationService.RequireAsync(Permission.AuditRead, ct);

        var filter = request.Filter with
        {
            Page = request.Filter.Page < 1 ? 1 : request.Filter.Page,
            PageSize = Math.Clamp(request.Filter.PageSize, 1, MaxPageSize),
        };

        var query = Filtered(db, filter);

        var total = await query.CountAsync(ct);

        // ★ ORDERED BY TIME, THEN BY Id. Bulk writes stamp many rows with the SAME timestamp — the
        // import job writes one per transaction in a single pass — and a sort on time alone leaves
        // their relative order up to the database, so a row could appear on two pages or on none.
        // Id is the tie-break because it is the insertion order and it is unique.
        var rows = await query
            .OrderByDescending(l => l.TimestampUtc)
            .ThenByDescending(l => l.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(l => new AuditLogRowDto(
                l.Id,
                l.TimestampUtc,
                l.ActorEmail,
                l.Action,
                l.ResourceType,
                l.ResourceId,
                l.ResourceDisplayName,
                // Computed in SQL so the flag describes the row the reader is looking at, and no
                // payload is dragged across the wire just to decide whether a chevron is shown.
                (l.BeforeJson != null && l.BeforeJson != "")
                    || (l.AfterJson != null && l.AfterJson != "")
                    || (l.Metadata != null && l.Metadata != "")))
            .ToListAsync(ct);

        return Result<AuditLogPageDto>.Success(
            new AuditLogPageDto(rows, filter.Page, filter.PageSize, total));
    }

    /// <summary>
    /// The one predicate. Used by the page, by its count, and by nothing else.
    ///
    /// ★★ <c>To</c> IS INCLUSIVE OF ITS WHOLE DAY, and that is a deliberate off-by-one. `TimestampUtc`
    /// is an instant; a reader who puts the same date in both boxes means "what happened that day",
    /// and comparing against midnight would return the empty set for the most obvious query anyone
    /// makes of an audit trail. So the upper bound is the START of the following day, exclusive.
    ///
    /// ★ THE ACTOR MATCH IS EXACT, NOT A CONTAINS. The values come from a dropdown the server
    /// populated with the actors that exist; a substring match would let "a@x.com" also select
    /// "za@x.com" and quietly attribute one person's actions to another.
    ///
    /// ★★ "THE SYSTEM" TRAVELS AS A WORD, NOT AS AN EMPTY STRING. Rows written by a background job
    /// carry an empty ActorEmail, so asking for them by sending `""` would be indistinguishable from
    /// sending no filter at all — the guard below reads it as absent and every row comes back while
    /// the dropdown claims to be filtering. <see cref="SystemActor"/> is a value no address can equal.
    /// </summary>
    internal static IQueryable<AuditLog> Filtered(IApplicationDbContext db, AuditLogFilter filter)
    {
        var query = db.AuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Action))
            query = query.Where(l => l.Action == filter.Action);

        if (filter.Actor == SystemActor)
            query = query.Where(l => l.ActorEmail == null || l.ActorEmail == "");
        else if (!string.IsNullOrWhiteSpace(filter.Actor))
            query = query.Where(l => l.ActorEmail == filter.Actor);

        if (filter.From.HasValue)
        {
            var from = filter.From.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(l => l.TimestampUtc >= from);
        }

        if (filter.To.HasValue)
        {
            var toExclusive = filter.To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(l => l.TimestampUtc < toExclusive);
        }

        return query;
    }
}
