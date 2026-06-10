using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.PayRuns;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.PayRuns;

public sealed class ApprovePayRunHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid)
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
            return Result.Success();
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
