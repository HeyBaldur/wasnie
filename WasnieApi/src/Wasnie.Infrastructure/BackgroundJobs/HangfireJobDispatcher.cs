using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Infrastructure.Identity;

namespace Wasnie.Infrastructure.BackgroundJobs;

/// <summary>
/// Single Hangfire entry-point for all background jobs. Sets tenant context from the
/// job payload BEFORE resolving any service that touches the database.
/// </summary>
public sealed class HangfireJobDispatcher(
    BackgroundJobTenantContext tenantCtx,
    IServiceProvider sp,
    ILogger<HangfireJobDispatcher> logger)
{
    // CancellationToken parameter is required by Hangfire for cooperative cancellation.
    public async Task ExecuteAsync(
        string handlerTypeName,
        string payloadJson,
        Guid tenantId,
        string userId,
        string userEmail,
        Guid jobTrackingId,
        CancellationToken cancellationToken = default)
    {
        // Set tenant FIRST — ApplicationDbContext.CurrentTenantId is lazy-evaluated per-query,
        // so this must happen before any service that accesses the database is resolved or used.
        tenantCtx.SetTenant(tenantId);

        logger.LogInformation(
            "Background job {JobId} starting. Handler: {Handler}. Tenant: {TenantId}. User: {UserId}",
            jobTrackingId, handlerTypeName, tenantId, userId);

        var jobService = sp.GetRequiredService<IBackgroundJobService>();

        await jobService.MarkRunningAsync(jobTrackingId, cancellationToken);

        try
        {
            var handlerType = Type.GetType(handlerTypeName)
                ?? throw new InvalidOperationException($"Handler type not found: '{handlerTypeName}'.");

            var handler = (IJobHandlerBase)sp.GetRequiredService(handlerType);
            var context = new JobContext(jobTrackingId, jobService);

            await handler.ExecuteRawAsync(payloadJson, context, cancellationToken);

            await jobService.MarkCompletedAsync(jobTrackingId, cancellationToken);

            logger.LogInformation(
                "Background job {JobId} completed successfully.", jobTrackingId);
        }
        catch (Exception ex)
        {
            await jobService.MarkFailedAsync(jobTrackingId, ex.Message, cancellationToken);

            logger.LogError(ex,
                "Background job {JobId} failed: {Message}", jobTrackingId, ex.Message);

            throw; // Re-throw so Hangfire marks the job as failed and can retry.
        }
    }
}
