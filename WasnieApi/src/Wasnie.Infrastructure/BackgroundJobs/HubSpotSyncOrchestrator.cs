using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Integrations.HubSpot;

namespace Wasnie.Infrastructure.BackgroundJobs;

/// <summary>
/// Phase-3 recurring orchestrator: the single hourly (configurable) entry point that fans the HubSpot
/// incremental sync OUT to one job per connected tenant.
///
/// It does NOT touch the CRM or create transactions itself — it only lists tenants and schedules per-tenant
/// jobs with a STAGGERED delay (anti thundering-herd: 1000 tenants don't hit the backend/DB at once). Each
/// tenant is its own Hangfire job, so one tenant failing never aborts the others and Hangfire retries only
/// the one that failed.
///
/// Cross-tenant by nature: it reads HubSpotConnections with the tenant query filter IGNORED, so it needs no
/// ambient tenant (and never evaluates CurrentTenantId).
/// </summary>
public sealed class HubSpotSyncOrchestrator(
    IApplicationDbContext db,
    IBackgroundJobClient jobClient,
    IOptions<HubSpotSyncOptions> options,
    ILogger<HubSpotSyncOrchestrator> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("HubSpot auto-sync disabled (HubSpotSync:Enabled=false); orchestrator no-op.");
            return;
        }

        var tenantIds = await db.HubSpotConnections
            .IgnoreQueryFilters()
            .Where(c => c.Status == HubSpotConnectionStatus.Connected)
            .Select(c => c.TenantId)
            .ToListAsync(cancellationToken);

        if (tenantIds.Count == 0)
        {
            logger.LogInformation("HubSpot auto-sync: no connected tenants to sync.");
            return;
        }

        var stagger = Math.Max(0, opts.TenantStaggerSeconds);
        for (var i = 0; i < tenantIds.Count; i++)
        {
            var tenantId = tenantIds[i];
            // Staggered fan-out: tenant N runs N×stagger seconds after now. Separate jobs ⇒ isolated failures.
            jobClient.Schedule<HubSpotTenantSyncJob>(
                j => j.SyncTenantAsync(tenantId, CancellationToken.None),
                TimeSpan.FromSeconds((long)i * stagger));
        }

        logger.LogInformation(
            "HubSpot auto-sync: scheduled {Count} per-tenant job(s), staggered {Stagger}s apart.",
            tenantIds.Count, stagger);
    }
}
