using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Application.Common.Models;

public sealed class JobContext(Guid jobId, IBackgroundJobService jobService)
{
    public Guid JobId => jobId;

    public Task ReportProgressAsync(int current, int total, CancellationToken ct = default) =>
        jobService.UpdateProgressAsync(jobId, current, total, ct);
}
