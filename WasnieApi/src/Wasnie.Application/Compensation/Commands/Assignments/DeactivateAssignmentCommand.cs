using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Assignments;

public sealed record DeactivateAssignmentCommand(Guid AssignmentId) : IRequest<Result>;
