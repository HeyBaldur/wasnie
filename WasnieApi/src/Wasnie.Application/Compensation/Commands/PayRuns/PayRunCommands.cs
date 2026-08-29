using MediatR;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Compensation.Commands.Payouts;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.PayRuns;

public sealed record CalculatePayRunCommand(
    DateOnly PeriodStart,
    DateOnly PeriodEnd) : IRequest<Result<CalculatePayRunResult>>;

/// <param name="Diagnostics">
/// What the engine did, so the screen can say WHY nothing was created instead of guessing. Passed
/// straight through from the calculation — this handler adds no reason of its own.
/// </param>
public sealed record CalculatePayRunResult(
    Guid PayRunId,
    int PayoutsCreated,
    IReadOnlyList<PayoutConflict> Conflicts,
    IReadOnlyList<PayoutWarning> Warnings,
    PayoutRunDiagnostics Diagnostics,
    bool IsSupplemental = false,
    int SupplementalSequence = 0);

public sealed record ApprovePayRunCommand(Guid PayRunId) : IRequest<Result>;

public sealed record MarkPayRunPaidCommand(Guid PayRunId) : IRequest<Result<PaymentBlockResult?>>;

public sealed record ReopenPayRunCommand(Guid PayRunId) : IRequest<Result>;

public sealed record DeletePayRunDraftCommand(Guid PayRunId) : IRequest<Result>;
