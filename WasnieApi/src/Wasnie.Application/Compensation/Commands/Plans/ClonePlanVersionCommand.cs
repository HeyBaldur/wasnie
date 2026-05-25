using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Plans;

public sealed record ClonePlanVersionCommand(Guid PlanId) : IRequest<Result<PlanDto>>;
