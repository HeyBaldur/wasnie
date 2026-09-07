using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Audit.DTOs;
using Wasnie.Application.Audit.Queries;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Audit.Handlers;

/// <summary>
/// The evidence behind one audit row (KAN-19).
///
/// ★ ITS OWN REQUEST, NOT A FIELD ON THE LIST. `BeforeJson` / `AfterJson` / `Metadata` are unbounded
/// text — the orphan-account closure writes every closed credit id with its amount — and shipping
/// them for 25 rows to show one would put the page's weight at the mercy of its noisiest action.
///
/// ★ A MISSING ROW IS Failure, NOT AN EMPTY DETAIL. On an audit screen, "there is nothing here" and
/// "this row is not yours to read" must not render as the same blank panel (§B3): the id is a
/// tenant's own, and the global query filter is what makes another tenant's id a miss rather than a
/// leak.
/// </summary>
public sealed class GetAuditLogDetailHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetAuditLogDetailQuery, Result<AuditLogDetailDto>>
{
    public async Task<Result<AuditLogDetailDto>> Handle(
        GetAuditLogDetailQuery request, CancellationToken ct)
    {
        await authorizationService.RequireAsync(Permission.AuditRead, ct);

        var row = await db.AuditLogs
            .Where(l => l.Id == request.Id)
            .Select(l => new AuditLogDetailDto(
                l.Id,
                l.TimestampUtc,
                l.ActorEmail,
                l.ActorUserId,
                l.Action,
                l.ResourceType,
                l.ResourceId,
                l.ResourceDisplayName,
                l.BeforeJson,
                l.AfterJson,
                l.Metadata,
                l.CorrelationId,
                l.IpAddress,
                l.UserAgent))
            .FirstOrDefaultAsync(ct);

        return row is null
            ? Result<AuditLogDetailDto>.Failure("AUDIT.DETAIL_NOT_FOUND")
            : Result<AuditLogDetailDto>.Success(row);
    }
}
