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

    /// <summary>
    /// Signals the job to cancel (sets state to Cancelling) and instructs Hangfire to abort execution.
    /// Returns false if the job is not found or already in a terminal state.
    /// </summary>
    Task<bool> CancelJobAsync(Guid jobId, Guid tenantId, CancellationToken ct = default);

    Task MarkCancelledAsync(Guid jobId, CancellationToken ct = default);
}
