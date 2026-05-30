using Microsoft.Extensions.Logging;
using Wasnie.Application.BackgroundJobs;
using Wasnie.Application.Common.Models;

namespace Wasnie.Infrastructure.BackgroundJobs;

public sealed record PingPayload(string Message = "ping");

/// <summary>
/// No-op job used to smoke-test the background job foundation.
/// Reports 0 → 50 → 100 progress and completes.
/// </summary>
public sealed class PingJobHandler(ILogger<PingJobHandler> logger) : JobHandlerBase<PingPayload>
{
    public override async Task HandleAsync(PingPayload payload, JobContext context, CancellationToken ct)
    {
        logger.LogInformation("PingJob {JobId}: {Message}", context.JobId, payload.Message);

        await context.ReportProgressAsync(0, 100, ct);
        await Task.Delay(50, ct);

        await context.ReportProgressAsync(50, 100, ct);
        await Task.Delay(50, ct);

        await context.ReportProgressAsync(100, 100, ct);

        logger.LogInformation("PingJob {JobId} complete.", context.JobId);
    }
}
