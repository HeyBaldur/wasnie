using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Integrations.Crm;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Integrations.HubSpot;
using Wasnie.Infrastructure.Identity;

namespace Wasnie.Infrastructure.BackgroundJobs;

/// <summary>
/// Phase-3 per-tenant incremental sync. Scheduled (staggered) by <see cref="HubSpotSyncOrchestrator"/>;
/// one Hangfire job per tenant. Reads ONLY the deals changed since the tenant's checkpoint and feeds them
/// to the SHARED <see cref="ICrmDealReconciler"/> — the exact same create-via-guard + drift logic the
/// manual import uses. Nothing is re-implemented here.
///
/// Money path: the reconciler creates Pending transactions and auto-voids drifted Pending ones; it NEVER
/// touches Calculated/Paid (Rule 10). The checkpoint advances ONLY on a clean run, using the run's START
/// instant — so a deal changed mid-run is re-seen next time (idempotent overlap, never a gap). A failure
/// leaves the checkpoint untouched; the next run safely re-processes the same window (guard + drift are
/// idempotent).
/// </summary>
public sealed class HubSpotTenantSyncJob(
    BackgroundJobTenantContext tenantCtx,
    IApplicationDbContext db,
    ICrmDealSource dealSource,
    ICrmDealReconciler reconciler,
    IDealLostReconciler dealLostReconciler,
    IClock clock,
    ILogger<HubSpotTenantSyncJob> logger)
{
    // Non-interactive actor stamped on transactions/audit created by the automatic sync.
    private const string SystemActor = "hubspot-auto-sync";

    public async Task SyncTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        // MUST set the tenant before any DB access — ApplicationDbContext.CurrentTenantId is lazy per-query.
        tenantCtx.SetTenant(tenantId);

        var connection = await db.HubSpotConnections
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        // Skip tenants that are not currently Connected (NeedsReconnect / Disconnected / gone) — don't try,
        // don't break. Their UI already explains the state.
        if (connection is null || connection.Status != HubSpotConnectionStatus.Connected)
        {
            logger.LogInformation(
                "HubSpot auto-sync skipped tenant {TenantId}: {Status}.",
                tenantId, connection?.Status.ToString() ?? "NoConnection");
            return;
        }

        var runStartedAt = clock.UtcNowOffset;
        // First ever run (no checkpoint) → floor at ConnectedAt: incremental from connection, NOT a full
        // history re-pull. The manual "Import deals" backfill covers pre-connection deals.
        var since = connection.LastSyncedAt ?? connection.ConnectedAt;

        IReadOnlyList<CrmDeal> deals;
        IReadOnlyList<CrmOwner> owners;
        string? defaultCurrency;
        try
        {
            deals = await dealSource.GetClosedWonDealsModifiedSinceAsync(tenantId, since, cancellationToken);
            owners = await dealSource.GetOwnersAsync(tenantId, cancellationToken);
            defaultCurrency = await dealSource.GetDefaultCurrencyAsync(tenantId, cancellationToken);
        }
        catch (CrmNotConnectedException)
        {
            // The token provider already moved the connection to NeedsReconnect (or it vanished). Don't
            // advance the checkpoint, don't crash the orchestration — skip; the UI surfaces "needs reconnect".
            logger.LogWarning("HubSpot auto-sync: tenant {TenantId} connection unusable; skipped this run.", tenantId);
            return;
        }

        var now = clock.UtcNowOffset;
        var result = await reconciler.ReconcileAsync(
            tenantId, dealSource.SourceName, deals, owners, defaultCurrency,
            SystemActor, SystemActor, now, cancellationToken);

        // Reverse reconciliation: detect already-credited deals that are NO LONGER closed-won (won→lost).
        // The forward pass above cannot see these (they drop out of the closed-won search). Read + alert only.
        // A CRM read failure here must NOT fail the whole sync or roll back the forward work — log and skip.
        var dealLostCount = 0;
        try
        {
            dealLostCount = await dealLostReconciler.ReconcileAsync(
                tenantId, dealSource.SourceName, SystemActor, SystemActor, now, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "HubSpot auto-sync: deal-lost reconciliation failed for tenant {TenantId}; forward sync kept, will retry next run.",
                tenantId);
        }

        // Success → advance the checkpoint to the run START instant (never backwards).
        connection.AdvanceSyncCheckpoint(runStartedAt, now);

        // One audit entry per tenant per successful run (observability — Rule 5 / PASO 3).
        db.AuditLogs.Add(AuditLog.Create(
            tenantId: tenantId,
            timestampUtc: now.UtcDateTime,
            actorUserId: SystemActor,
            actorEmail: SystemActor,
            action: AuditActions.CrmAutoSyncCompleted,
            resourceType: ResourceTypes.Integration,
            resourceId: "HubSpot",
            resourceDisplayName: "HubSpot automatic sync",
            beforeJson: JsonSerializer.Serialize(new { since }),
            afterJson: JsonSerializer.Serialize(new
            {
                dealsRead = result.DealsRead,
                created = result.Created,
                driftAutoResolved = result.DriftAutoResolved,
                driftAlertsRaised = result.DriftAlertsRaised,
                dealLostAlerts = dealLostCount,
                newOwnerMappings = result.NewOwnerMappings,
                checkpoint = runStartedAt,
            })));

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "HubSpot auto-sync tenant {TenantId}: read {Read}, created {Created}, driftAuto {Auto}, alerts {Alerts}.",
            tenantId, result.DealsRead, result.Created, result.DriftAutoResolved, result.DriftAlertsRaised);
    }
}
