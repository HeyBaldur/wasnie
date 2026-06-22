using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Integrations.HubSpot;

/// <summary>Disconnects the tenant's HubSpot connection: clears stored tokens, keeps the row for audit.</summary>
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
