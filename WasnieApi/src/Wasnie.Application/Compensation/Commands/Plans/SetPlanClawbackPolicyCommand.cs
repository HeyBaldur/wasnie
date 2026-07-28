using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Plans;

/// <summary>
/// Both null clears the policy and returns the plan to "never claws back".
/// </summary>
public sealed record SetPlanClawbackPolicyCommand(
    Guid PlanId,
    int? MaturationDays,
    decimal? CapPercent) : IRequest<Result>;
