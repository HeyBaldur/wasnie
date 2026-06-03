using Wasnie.Domain.BackgroundJobs;

namespace Wasnie.Application.Common.Models;

public sealed record JobStatusDto(
    Guid Id,
    JobState State,
    int ProgressCurrent,
    int ProgressTotal,
    string? ErrorMessage,
    DateTime EnqueuedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? ResultSummary
);
