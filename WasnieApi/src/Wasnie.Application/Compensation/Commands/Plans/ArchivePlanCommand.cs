using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Plans;

public sealed record ArchivePlanCommand(Guid PlanId) : IRequest<Result>;
