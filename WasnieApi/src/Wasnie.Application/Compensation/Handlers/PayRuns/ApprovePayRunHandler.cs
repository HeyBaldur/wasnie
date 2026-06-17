using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.PayRuns;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.PayRuns;

public sealed class ApprovePayRunHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuditService audit)
    : IRequestHandler<ApprovePayRunCommand, Result>
{
    public async Task<Result> Handle(ApprovePayRunCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsApprove, cancellationToken);

        var payRun = await db.PayRuns
            .FirstOrDefaultAsync(r => r.Id == request.PayRunId, cancellationToken);

        if (payRun is null)
            return Result.Failure("Pay run not found.");

        try
        {
            var actor = currentUser.Email ?? currentUser.UserId ?? "system";
            var now = clock.UtcNowOffset;

            // Capture overlapping Approved/Paid runs before transition (for audit).
            var overlappingIds = await db.PayRuns
                .Where(r => r.Id != request.PayRunId
                         && (r.Status == PayRunStatus.Approved || r.Status == PayRunStatus.Paid)
                         && r.PeriodStart <= payRun.PeriodEnd
                         && r.PeriodEnd >= payRun.PeriodStart)
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);

            payRun.Approve(actor, now, guid.NewGuid());

            // Cascade: Calculated → Approved for non-Disputed payouts in this run.
            var payouts = await db.CompensationPayouts
                .Where(p => p.PayRunId == request.PayRunId
                         && p.Status == CompensationPayoutStatus.Calculated)
                .ToListAsync(cancellationToken);

            foreach (var payout in payouts)
                payout.Approve(actor, now, guid.NewGuid());

            // Recompute roll-ups to include all approved payouts.
            var allRunPayouts = await db.CompensationPayouts
                .Where(p => p.PayRunId == request.PayRunId
                         && p.Status != CompensationPayoutStatus.Disputed)
                .ToListAsync(cancellationToken);

            payRun.UpdateRollUps(allRunPayouts);

            await db.SaveChangesAsync(cancellationToken);

            if (overlappingIds.Count > 0)
            {
                await audit.LogAsync(new AuditEntry(
                    TenantId: payRun.TenantId,
                    Action: AuditActions.PayRunApprovedWithOverlap,
                    ResourceType: "PayRun",
                    ResourceId: request.PayRunId.ToString(),
                    ActorUserId: currentUser.UserId ?? actor,
                    ActorEmail: actor,
                    Metadata: new Dictionary<string, string>
                    {
                        ["overlappingPayRunIds"] = string.Join(",", overlappingIds),
                        ["overlapCount"] = overlappingIds.Count.ToString(),
                    }), cancellationToken);
            }

            return Result.Success();
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
