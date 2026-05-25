using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Plans;

public sealed record CreatePlanCommand(
    string Name,
    string Description,
    DateOnly EffectiveStart,
    DateOnly EffectiveEnd,
    string Currency) : IRequest<Result<PlanDto>>;
