using Hangfire;
using Wasnie.Application.Integrations.Crm;

namespace Wasnie.Infrastructure.BackgroundJobs;

/// <inheritdoc cref="ICrmSyncScheduler"/>
public sealed class HangfireCrmSyncScheduler(IBackgroundJobClient jobClient) : ICrmSyncScheduler
{
    public void EnqueueTenantSyncNow(Guid tenantId) =>
        // Same per-tenant job the orchestrator schedules — just fired immediately instead of on the cron.
        jobClient.Enqueue<HubSpotTenantSyncJob>(j => j.SyncTenantAsync(tenantId, CancellationToken.None));
}
