using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Integrations.Crm;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Integrations.HubSpot;

namespace Wasnie.Application.Integrations.HubSpot;

/// <summary>
/// Phase 3 "Sync now": enqueue an immediate incremental sync for the current tenant, in addition to the
/// recurring schedule. Only enqueues — the actual work runs on the same per-tenant background job (idempotent,
/// drift-safe). Requires a usable (Connected) HubSpot connection.
/// </summary>
public sealed record TriggerHubSpotSyncCommand : IRequest<Result<Unit>>;

public sealed class TriggerHubSpotSyncHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    IAuthorizationService authorizationService,
    IPaidPlanGate paidPlanGate,
    ICrmSyncScheduler scheduler)
    : IRequestHandler<TriggerHubSpotSyncCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(TriggerHubSpotSyncCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.IntegrationsManage, cancellationToken);
        // Metered capability: the plan is checked after the permission, so an admin on Free is
        // told the truth ("not in your plan") instead of a bare Forbidden. Frozen, not deleted —
        // a downgraded tenant keeps its stored connection and resumes on upgrade.
        await paidPlanGate.RequirePaidPlanAsync("The HubSpot integration", cancellationToken);

        var tenantId = tenantContext.TenantId;
        var connection = await db.HubSpotConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (connection is null || connection.Status != HubSpotConnectionStatus.Connected)
            return Result<Unit>.Failure("Connect HubSpot before syncing.");

        scheduler.EnqueueTenantSyncNow(tenantId);
        return Result<Unit>.Success(Unit.Value);
    }
}
