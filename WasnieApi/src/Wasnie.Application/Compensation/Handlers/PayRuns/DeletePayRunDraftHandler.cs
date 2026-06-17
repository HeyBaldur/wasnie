using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.PayRuns;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.PayRuns;

public sealed class DeletePayRunDraftHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ICurrentUserService currentUser,
    IAuditService audit)
    : IRequestHandler<DeletePayRunDraftCommand, Result>
{
    public async Task<Result> Handle(DeletePayRunDraftCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsDeleteDraft, cancellationToken);

        var payRun = await db.PayRuns
            .FirstOrDefaultAsync(r => r.Id == request.PayRunId, cancellationToken);

        if (payRun is null)
            return Result.Failure("Pay run not found.");

        // GOLDEN RULE: only Draft pay runs may be deleted.
        // Approved and Paid are permanent records — backend enforces this unconditionally.
        if (payRun.Status != PayRunStatus.Draft)
            return Result.Failure(
                "Only Draft pay runs can be deleted. Approved and Paid pay runs are permanently locked.");

        var actor = currentUser.Email ?? currentUser.UserId ?? "system";

        // FK is OnDelete(Restrict): must remove payouts before removing the run.
        // Lines are covered by the PayoutLine → CompensationPayout Cascade FK.
        var payouts = await db.CompensationPayouts
            .Where(p => p.PayRunId == request.PayRunId)
            .ToListAsync(cancellationToken);

        db.CompensationPayouts.RemoveRange(payouts);
        db.PayRuns.Remove(payRun);

        await db.SaveChangesAsync(cancellationToken);

        await audit.LogAsync(new AuditEntry(
            TenantId: payRun.TenantId,
            Action: AuditActions.PayRunDraftDeleted,
            ResourceType: "PayRun",
            ResourceId: request.PayRunId.ToString(),
            ActorUserId: currentUser.UserId ?? actor,
            ActorEmail: actor,
            Before: new
            {
                PeriodStart = payRun.PeriodStart.ToString("yyyy-MM-dd"),
                PeriodEnd = payRun.PeriodEnd.ToString("yyyy-MM-dd"),
                PayeeCount = payRun.PayeeCount,
                TotalAmounts = payRun.TotalAmounts,
            }), cancellationToken);

        return Result.Success();
    }
}
