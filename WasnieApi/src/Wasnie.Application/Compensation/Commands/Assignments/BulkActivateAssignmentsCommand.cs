using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Assignments;

public sealed record BulkActivateAssignmentsCommand(IReadOnlyList<Guid> AssignmentIds) : IRequest<Result<BulkAssignmentOperationResult>>;

public sealed record BulkAssignmentOperationResult(int Affected, IReadOnlyList<string> Errors);
