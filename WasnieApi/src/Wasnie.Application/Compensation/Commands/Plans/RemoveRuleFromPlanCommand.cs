using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Plans;

public sealed record RemoveRuleFromPlanCommand(Guid PlanId, Guid RuleId) : IRequest<Result>;
