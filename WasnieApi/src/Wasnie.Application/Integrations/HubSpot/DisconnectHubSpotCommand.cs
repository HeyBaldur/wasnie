using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Integrations.HubSpot;

/// <summary>
/// Disconnects the tenant's HubSpot connection: clears stored tokens, keeps the row for audit.
///
/// ★ DELIBERATELY NOT behind the paid-plan gate, unlike every other operation here. A tenant that
/// downgraded to Free still holds encrypted HubSpot tokens in our database; refusing to let them
/// revoke those would mean billing state deciding whether someone may delete their own credentials.
/// Every gated operation SPENDS (outbound CRM calls, sync, storage) — this one only ever gives back.
/// </summary>
public sealed record DisconnectHubSpotCommand : IRequest<Result<Unit>>;

public sealed class DisconnectHubSpotHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IAuthorizationService authorizationService,
    IAuditService auditService,
    IClock clock)
    : IRequestHandler<DisconnectHubSpotCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(DisconnectHubSpotCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.IntegrationsManage, cancellationToken);

        var connection = await db.HubSpotConnections
            .FirstOrDefaultAsync(c => c.TenantId == tenantContext.TenantId, cancellationToken);

        if (connection is null)
            return Result<Unit>.Failure("There is no HubSpot connection to disconnect.");

        var userId = currentUser.UserId ?? "system";
        connection.Disconnect("Disconnected by user.", userId, clock.UtcNowOffset);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantContext.TenantId,
                Action: AuditActions.HubSpotDisconnected,
                ResourceType: ResourceTypes.Integration,
                ResourceId: "HubSpot",
                ActorUserId: userId,
                ActorEmail: currentUser.Email ?? string.Empty,
                DisplayName: "HubSpot"), cancellationToken);
        }
        catch { /* audit must not block (Rule 5.3.3) */ }

        return Result<Unit>.Success(Unit.Value);
    }
}
