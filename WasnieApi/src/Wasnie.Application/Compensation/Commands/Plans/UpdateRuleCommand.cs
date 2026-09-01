using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Rules;

namespace Wasnie.Application.Compensation.Commands.Plans;

public sealed record UpdateRuleCommand(
    Guid PlanId,
    Guid RuleId,
    string Name,
    int SortOrder,
    Measurement Measurement,
    RateTableRequest RateTable,
    Trigger? Trigger,
    Modifier? Modifier,
    Cap? Cap,
    Floor? Floor) : IRequest<Result<RuleDto>>;
