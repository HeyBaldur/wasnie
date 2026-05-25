using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Rules;

namespace Wasnie.Application.Compensation.Commands.Plans;

public sealed record AddRuleToPlanCommand(
    Guid PlanId,
    string Name,
    int SortOrder,
    Measurement Measurement,
    RateTable RateTable,
    Trigger? Trigger,
    Modifier? Modifier,
    Cap? Cap,
    Floor? Floor) : IRequest<Result<RuleDto>>;
