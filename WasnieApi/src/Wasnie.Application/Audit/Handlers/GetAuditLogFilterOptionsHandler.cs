using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Audit.DTOs;
using Wasnie.Application.Audit.Queries;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Audit.Handlers;

/// <summary>
/// What the two filter dropdowns offer (KAN-19).
///
/// ★★ READ FROM THE LOG, NEVER FROM <c>AuditActions</c>. Both directions of that choice were
/// measured in the reference tenant during KAN-19's Paso 0:
///
/// • The log holds codes with NO constant. `transaction_voided` — 18 rows, still written today by
///   <c>VoidTransactionCommand</c> and <c>BulkVoidTransactionsHandler</c> — would be missing from a
///   constant-derived list, leaving those rows unreachable by any filter.
/// • 18 of the 96 constants have never been written at all (quotas, assignments, rule add/edit/
///   remove). Offering them would produce a filter that always returns nothing, which a reader
///   fairly interprets as "this never happened" rather than "this was never recorded".
///
/// The second of those is a real gap in the trail, not a display problem, and it is not this
/// handler's to fix: it is raised as its own ticket with the evidence (§10).
///
/// ★ THE SYSTEM ACTOR IS NOT OFFERED AS AN EMPTY OPTION. Rows written by a job carry an empty
/// ActorEmail; an empty entry in a dropdown is unclickable and unreadable, so it is dropped here and
/// the screen offers "System" as its own choice mapped to the empty string.
/// </summary>
public sealed class GetAuditLogFilterOptionsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetAuditLogFilterOptionsQuery, Result<AuditLogFilterOptionsDto>>
{
    public async Task<Result<AuditLogFilterOptionsDto>> Handle(
        GetAuditLogFilterOptionsQuery request, CancellationToken ct)
    {
        await authorizationService.RequireAsync(Permission.AuditRead, ct);

        var actions = await db.AuditLogs
            .Select(l => l.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(ct);

        var actors = await db.AuditLogs
            .Where(l => l.ActorEmail != null && l.ActorEmail != "")
            .Select(l => l.ActorEmail)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(ct);

        return Result<AuditLogFilterOptionsDto>.Success(
            new AuditLogFilterOptionsDto(actions, actors));
    }
}
