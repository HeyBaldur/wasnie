using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Assignments;

public sealed record UpdateAssignmentNotesCommand(Guid AssignmentId, string? Notes) : IRequest<Result>;
