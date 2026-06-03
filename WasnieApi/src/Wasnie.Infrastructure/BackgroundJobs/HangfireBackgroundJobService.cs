using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Domain.BackgroundJobs;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.Infrastructure.BackgroundJobs;

public sealed class HangfireBackgroundJobService(
    IBackgroundJobClient hangfireClient,
    ApplicationDbContext db,
    ILogger<HangfireBackgroundJobService> logger)
    : IBackgroundJobService
{
    public async Task<Guid> EnqueueAsync<TPayload>(
        TPayload payload,
        Guid tenantId,
        string userId,
        string userEmail,
        CancellationToken ct = default)
        where TPayload : notnull
    {
        var trackingId = Guid.NewGuid();
        var handlerTypeName = typeof(IJobHandler<TPayload>).AssemblyQualifiedName
            ?? throw new InvalidOperationException($"Cannot resolve AssemblyQualifiedName for IJobHandler<{typeof(TPayload).Name}>.");

        // Persist the record first so it exists before Hangfire can execute the job.
        var record = BackgroundJobRecord.CreatePending(trackingId, tenantId, handlerTypeName);
        db.BackgroundJobRecords.Add(record);
        await db.SaveChangesAsync(ct);

        var payloadJson = JsonSerializer.Serialize(payload);

        var hangfireJobId = hangfireClient.Enqueue<HangfireJobDispatcher>(
            d => d.ExecuteAsync(
                handlerTypeName, payloadJson, tenantId, userId, userEmail,
                trackingId, CancellationToken.None));

        record.SetHangfireJobId(hangfireJobId);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Enqueued background job {JobId} (Hangfire: {HangfireJobId}) for tenant {TenantId}",
            trackingId, hangfireJobId, tenantId);

        return trackingId;
    }

    public async Task<JobStatusDto?> GetJobStatusAsync(Guid jobId, Guid tenantId, CancellationToken ct = default)
    {
        // Query filter enforces TenantId == CurrentTenantId; pass tenantId explicitly as
        // a belt-and-suspenders cross-tenant guard for callers outside request scope.
        var record = await db.BackgroundJobRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == jobId && r.TenantId == tenantId, ct);

        return record is null ? null : ToDto(record);
    }

    public async Task MarkRunningAsync(Guid jobId, CancellationToken ct = default)
    {
        var record = await db.BackgroundJobRecords.FindAsync([jobId], ct)
            ?? throw new InvalidOperationException($"Background job record {jobId} not found.");
        record.MarkRunning();
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateProgressAsync(Guid jobId, int current, int total, CancellationToken ct = default)
    {
        var record = await db.BackgroundJobRecords.FindAsync([jobId], ct)
            ?? throw new InvalidOperationException($"Background job record {jobId} not found.");
        record.UpdateProgress(current, total);
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkCompletedAsync(Guid jobId, CancellationToken ct = default)
    {
        var record = await db.BackgroundJobRecords.FindAsync([jobId], ct)
            ?? throw new InvalidOperationException($"Background job record {jobId} not found.");
        record.MarkCompleted();
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkFailedAsync(Guid jobId, string errorMessage, CancellationToken ct = default)
    {
        var record = await db.BackgroundJobRecords.FindAsync([jobId], ct);
        if (record is null)
        {
            logger.LogWarning("Attempted to mark unknown job {JobId} as failed.", jobId);
            return;
        }
        record.MarkFailed(errorMessage);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> CancelJobAsync(Guid jobId, Guid tenantId, CancellationToken ct = default)
    {
        var record = await db.BackgroundJobRecords
            .FirstOrDefaultAsync(r => r.Id == jobId && r.TenantId == tenantId, ct);

        if (record is null) return false;

        // Only active jobs can be cancelled.
        if (record.State is not (JobState.Pending or JobState.Running)) return false;

        var hangfireJobId = record.HangfireJobId;
        record.RequestCancellation();
        await db.SaveChangesAsync(ct);

        // Signal Hangfire to abort the job (signals CancellationToken in the job method).
        if (!string.IsNullOrEmpty(hangfireJobId))
            hangfireClient.Delete(hangfireJobId);

        return true;
    }

    public async Task MarkCancelledAsync(Guid jobId, CancellationToken ct = default)
    {
        var record = await db.BackgroundJobRecords.FindAsync([jobId], ct);
        if (record is null) return;
        record.MarkCancelled();
        await db.SaveChangesAsync(ct);
    }

    private static JobStatusDto ToDto(BackgroundJobRecord r) =>
        new(r.Id, r.State, r.ProgressCurrent, r.ProgressTotal,
            r.ErrorMessage, r.EnqueuedAtUtc, r.StartedAtUtc, r.CompletedAtUtc);
}
