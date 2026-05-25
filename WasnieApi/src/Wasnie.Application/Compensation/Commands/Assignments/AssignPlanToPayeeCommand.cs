using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Assignments;

public sealed record AssignPlanToPayeeCommand(
    Guid PlanId,
    Guid PayeeId,
    DateOnly EffectiveStart,
    DateOnly EffectiveEnd) : IRequest<Result<PlanAssignmentDto>>;
