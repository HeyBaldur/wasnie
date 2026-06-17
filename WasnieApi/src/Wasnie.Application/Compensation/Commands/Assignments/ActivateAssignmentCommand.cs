using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Assignments;

public sealed record ActivateAssignmentCommand(Guid AssignmentId) : IRequest<Result>;
