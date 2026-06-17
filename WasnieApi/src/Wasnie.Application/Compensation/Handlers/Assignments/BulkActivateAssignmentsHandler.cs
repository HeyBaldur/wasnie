using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Assignments;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Assignments;

public sealed class BulkActivateAssignmentsHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuthorizationService authorizationService,
    IAuditService audit)
    : IRequestHandler<BulkActivateAssignmentsCommand, Result<BulkAssignmentOperationResult>>
{
    public async Task<Result<BulkAssignmentOperationResult>> Handle(
        BulkActivateAssignmentsCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.AssignmentsUpdate, cancellationToken);

        if (request.AssignmentIds.Count == 0)
            return Result<BulkAssignmentOperationResult>.Success(new BulkAssignmentOperationResult(0, []));

        var assignments = await db.PlanAssignments
            .Where(a => request.AssignmentIds.Contains(a.Id))
            .ToListAsync(cancellationToken);

        var now   = clock.UtcNowOffset;
        var actor = currentUser.UserId ?? "system";
        var affected = 0;
        var errors = new List<string>();

        foreach (var assignment in assignments)
        {
            try
            {
                assignment.Activate(actor, now, guid.NewGuid());
                affected++;
            }
            catch (DomainException ex)
            {
                errors.Add($"Assignment {assignment.Id} ({assignment.PayeeSnapshot.FullName}): {ex.Message}");
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        if (affected > 0 && assignments.Count > 0)
        {
            await audit.LogAsync(new AuditEntry(
                TenantId: assignments[0].TenantId,
                Action: AuditActions.AssignmentBulkActivated,
                ResourceType: "PlanAssignment",
                ResourceId: string.Join(",", request.AssignmentIds),
                ActorUserId: actor,
                ActorEmail: currentUser.Email ?? actor,
                Metadata: new Dictionary<string, string>
                {
                    ["affected"] = affected.ToString(),
                    ["total"] = assignments.Count.ToString(),
                }), cancellationToken);
        }

        return Result<BulkAssignmentOperationResult>.Success(new BulkAssignmentOperationResult(affected, errors));
    }
}
