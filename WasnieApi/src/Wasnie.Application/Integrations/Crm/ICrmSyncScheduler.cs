namespace Wasnie.Application.Integrations.Crm;

/// <summary>
/// Schedules CRM sync work on the background-job runtime. Keeps Hangfire out of the Application layer so an
/// on-demand "Sync now" can enqueue the SAME per-tenant incremental sync the recurring orchestrator fans
/// out to — no duplicated logic, just a different trigger.
/// </summary>
public interface ICrmSyncScheduler
{
    /// <summary>Enqueue an immediate one-off incremental sync for this tenant (on top of the schedule).</summary>
    void EnqueueTenantSyncNow(Guid tenantId);
}
