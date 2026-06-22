using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Credits;

public sealed record RecalculateCreditsCommand(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    Guid? PayeeId) : IRequest<Result<RecalculateCreditsResult>>, IMoneyCriticalCommand
{
    public string AuditAction => AuditActions.CreditsRecalculated;
    public string AuditResourceType => ResourceTypes.Credit;
    public string? AuditResourceId { get; set; }
    public string? AuditDisplayName => $"RecalculateCredits:{PeriodStart:yyyy-MM-dd}/{PeriodEnd:yyyy-MM-dd}";
}

public sealed record RecalculateCreditsResult(
    int SupersededCount,
    int SkippedPaidCount,
    IReadOnlyList<Guid> JobIds,
    int DeletedDraftCount = 0,
    IReadOnlyList<BlockingPayRunInfo>? BlockedByPayRuns = null);

public sealed record BlockingPayRunInfo(
    Guid PayRunId,
    string Status,
    DateOnly PeriodStart,
    DateOnly PeriodEnd);
