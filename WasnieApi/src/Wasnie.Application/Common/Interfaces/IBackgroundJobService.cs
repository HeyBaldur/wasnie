using Wasnie.Application.Common.Models;

namespace Wasnie.Application.Common.Interfaces;

public interface IBackgroundJobService
{
    Task<Guid> EnqueueAsync<TPayload>(
        TPayload payload,
        Guid tenantId,
        string userId,
        string userEmail,
        CancellationToken ct = default)
        where TPayload : notnull;

    Task<JobStatusDto?> GetJobStatusAsync(Guid jobId, Guid tenantId, CancellationToken ct = default);

    Task UpdateProgressAsync(Guid jobId, int current, int total, CancellationToken ct = default);
    Task MarkRunningAsync(Guid jobId, CancellationToken ct = default);
    Task MarkCompletedAsync(Guid jobId, CancellationToken ct = default);
    Task MarkFailedAsync(Guid jobId, string errorMessage, CancellationToken ct = default);
}
