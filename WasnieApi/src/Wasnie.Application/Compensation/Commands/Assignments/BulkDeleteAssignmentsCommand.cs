using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Assignments;

public sealed record BulkDeleteAssignmentsCommand(IReadOnlyList<Guid> AssignmentIds) : IRequest<Result<BulkDeleteAssignmentsResult>>;

public sealed record BulkDeleteAssignmentsResult(
    bool AllDeleted,
    int DeletedCount,
    IReadOnlyList<BlockedAssignmentDto> Blocked);

public sealed record BlockedAssignmentDto(
    Guid AssignmentId,
    string PayeeName,
    string PlanName,
    DateOnly EffectiveStart,
    DateOnly EffectiveEnd,
    string Reason);
