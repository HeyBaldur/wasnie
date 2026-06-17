using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Assignments;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Assignments;

public sealed class BulkDeleteAssignmentsHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IAuthorizationService authorizationService,
    IAuditService audit)
    : IRequestHandler<BulkDeleteAssignmentsCommand, Result<BulkDeleteAssignmentsResult>>
{
    public async Task<Result<BulkDeleteAssignmentsResult>> Handle(
        BulkDeleteAssignmentsCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.AssignmentsDelete, cancellationToken);

        if (request.AssignmentIds.Count == 0)
            return Result<BulkDeleteAssignmentsResult>.Success(
                new BulkDeleteAssignmentsResult(true, 0, []));

        var assignments = await db.PlanAssignments
            .Where(a => request.AssignmentIds.Contains(a.Id))
            .ToListAsync(cancellationToken);

        if (assignments.Count == 0)
            return Result<BulkDeleteAssignmentsResult>.Success(
                new BulkDeleteAssignmentsResult(true, 0, []));

        // Check each assignment for payout usage (period overlap = "used")
        var blockedIds = new List<Guid>();

        foreach (var a in assignments)
        {
            var periodStart = a.EffectivePeriod.Start;
            var periodEnd   = a.EffectivePeriod.End;

            var isUsed = await db.CompensationPayouts.AnyAsync(
                p => p.TenantId  == a.TenantId
                  && p.PayeeId   == a.PayeeId
                  && p.PlanId    == a.PlanId
                  && p.Period.Start <= periodEnd
                  && p.Period.End   >= periodStart,
                cancellationToken);

            if (isUsed)
                blockedIds.Add(a.Id);
        }

        // All-or-nothing: if any blocked, delete nothing
        if (blockedIds.Count > 0)
        {
            // Fetch plan names only for blocked items
            var blockedPlanIds  = assignments.Where(a => blockedIds.Contains(a.Id)).Select(a => a.PlanId).Distinct().ToList();
            var planNames       = await db.CompensationPlans
                .Where(pl => blockedPlanIds.Contains(pl.Id))
                .ToDictionaryAsync(pl => pl.Id, pl => pl.Name, cancellationToken);

            var blocked = assignments
                .Where(a => blockedIds.Contains(a.Id))
                .Select(a => new BlockedAssignmentDto(
                    AssignmentId:   a.Id,
                    PayeeName:      a.PayeeSnapshot.FullName,
                    PlanName:       planNames.TryGetValue(a.PlanId, out var pn) ? pn : a.PlanId.ToString(),
                    EffectiveStart: a.EffectivePeriod.Start,
                    EffectiveEnd:   a.EffectivePeriod.End,
                    Reason:         "ASSIGNMENTS.DELETE_BLOCKED_REASON"))
                .ToList();

            return Result<BulkDeleteAssignmentsResult>.Success(
                new BulkDeleteAssignmentsResult(false, 0, blocked));
        }

        // All safe — delete all
        db.PlanAssignments.RemoveRange(assignments);
        await db.SaveChangesAsync(cancellationToken);

        var tenantId = assignments[0].TenantId;
        var actor    = currentUser.UserId ?? "system";

        await audit.LogAsync(new AuditEntry(
            TenantId: tenantId,
            Action: AuditActions.AssignmentBulkDeleted,
            ResourceType: "PlanAssignment",
            ResourceId: string.Join(",", request.AssignmentIds),
            ActorUserId: actor,
            ActorEmail: currentUser.Email ?? actor,
            Metadata: new Dictionary<string, string>
            {
                ["deletedCount"] = assignments.Count.ToString(),
            }), cancellationToken);

        return Result<BulkDeleteAssignmentsResult>.Success(
            new BulkDeleteAssignmentsResult(true, assignments.Count, []));
    }
}
