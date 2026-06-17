using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Assignments;

public sealed record BulkDeactivateAssignmentsCommand(IReadOnlyList<Guid> AssignmentIds) : IRequest<Result<BulkAssignmentOperationResult>>;
