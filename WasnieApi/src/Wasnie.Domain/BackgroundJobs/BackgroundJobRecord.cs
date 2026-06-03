namespace Wasnie.Domain.BackgroundJobs;

public sealed class BackgroundJobRecord
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string HangfireJobId { get; private set; } = string.Empty;
    public string HandlerType { get; private set; } = string.Empty;
    public JobState State { get; private set; }
    public int ProgressCurrent { get; private set; }
    public int ProgressTotal { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime EnqueuedAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    private BackgroundJobRecord() { }

    public static BackgroundJobRecord CreatePending(Guid id, Guid tenantId, string handlerType) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            HandlerType = handlerType,
            State = JobState.Pending,
            EnqueuedAtUtc = DateTime.UtcNow,
        };

    public void SetHangfireJobId(string hangfireJobId) => HangfireJobId = hangfireJobId;

    public void MarkRunning()
    {
        State = JobState.Running;
        StartedAtUtc = DateTime.UtcNow;
    }

    public void UpdateProgress(int current, int total)
    {
        ProgressCurrent = current;
        ProgressTotal = total;
    }

    public void MarkCompleted()
    {
        State = JobState.Succeeded;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string errorMessage)
    {
        State = JobState.Failed;
        ErrorMessage = errorMessage;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void RequestCancellation()
    {
        if (State is not (JobState.Pending or JobState.Running))
            return; // no-op if already terminal
        State = JobState.Cancelling;
    }

    public void MarkCancelled()
    {
        State = JobState.Cancelled;
        CompletedAtUtc = DateTime.UtcNow;
    }
}
