using MediatR;
using Wasnie.Application.Compensation.Commands.Payouts;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.PayRuns;

public sealed record CalculatePayRunCommand(
    DateOnly PeriodStart,
    DateOnly PeriodEnd) : IRequest<Result<CalculatePayRunResult>>;

public sealed record CalculatePayRunResult(
    Guid PayRunId,
    int PayoutsCreated,
    IReadOnlyList<PayoutConflict> Conflicts,
    IReadOnlyList<PayoutWarning> Warnings);

public sealed record ApprovePayRunCommand(Guid PayRunId) : IRequest<Result>;

public sealed record MarkPayRunPaidCommand(Guid PayRunId) : IRequest<Result>;

public sealed record ReopenPayRunCommand(Guid PayRunId) : IRequest<Result>;

public sealed record DeletePayRunDraftCommand(Guid PayRunId) : IRequest<Result>;
