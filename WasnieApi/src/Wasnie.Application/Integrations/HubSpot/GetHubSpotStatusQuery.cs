using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Integrations.HubSpot;

/// <summary>Returns the tenant's HubSpot connection status for the UI. NEVER returns tokens.</summary>
public sealed record GetHubSpotStatusQuery : IRequest<Result<HubSpotConnectionStatusDto>>;

public sealed class GetHubSpotStatusHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    IAuthorizationService authorizationService,
    IPaidPlanGate paidPlanGate)
    : IRequestHandler<GetHubSpotStatusQuery, Result<HubSpotConnectionStatusDto>>
{
    public async Task<Result<HubSpotConnectionStatusDto>> Handle(
        GetHubSpotStatusQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.IntegrationsManage, cancellationToken);

        // ★ The ONLY HubSpot endpoint a Free tenant may still call, and on purpose: it reads our own
        // database, spends nothing on HubSpot, and is what the UI needs to render the locked card. A
        // 403 here would leave the page unable to say WHY it is locked.
        var requiresUpgrade = !await paidPlanGate.IsOnPaidPlanAsync(cancellationToken);

        var c = await db.HubSpotConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantContext.TenantId, cancellationToken);

        // No row = never connected.
        if (c is null)
            return Result<HubSpotConnectionStatusDto>.Success(new HubSpotConnectionStatusDto(
                "NeverConnected", null, null, null, null, null, RequiresUpgrade: requiresUpgrade));

        var disconnected = c.Status == Domain.Integrations.HubSpot.HubSpotConnectionStatus.Disconnected;
        return Result<HubSpotConnectionStatusDto>.Success(new HubSpotConnectionStatusDto(
            Status: c.Status.ToString(),
            PortalId: c.PortalId,
            StatusReason: c.StatusReason,
            ConnectedAt: disconnected ? null : c.ConnectedAt,
            ConnectedBy: c.ConnectedBy,
            DisconnectedAt: c.DisconnectedAt,
            LastSyncedAt: disconnected ? null : c.LastSyncedAt,
            CategoryPropertyName: c.CategoryPropertyName,
            RequiresUpgrade: requiresUpgrade));
    }
}
