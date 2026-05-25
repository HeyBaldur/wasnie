using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Plans;

public sealed record ActivatePlanCommand(Guid PlanId) : IRequest<Result>;
