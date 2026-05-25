using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Plans;

public sealed record DeletePlanCommand(Guid PlanId) : IRequest<Result>;
