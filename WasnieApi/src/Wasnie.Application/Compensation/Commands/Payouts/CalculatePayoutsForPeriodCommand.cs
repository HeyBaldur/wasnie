using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Payouts;

public sealed record CalculatePayoutsForPeriodCommand(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    Guid? PayeeIdFilter = null) : IRequest<Result<CalculatePayoutsResult>>;

public sealed record CalculatePayoutsResult(
    int PayoutsCreated,
    IReadOnlyList<PayoutConflict> Conflicts,
    IReadOnlyList<PayoutWarning> Warnings);

public sealed record PayoutConflict(
    Guid PayeeId,
    string PayeeName,
    Guid PlanId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status);

public sealed record PayoutWarning(
    Guid PayeeId,
    string PayeeName,
    Guid PlanId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int PendingTransactionCount);
