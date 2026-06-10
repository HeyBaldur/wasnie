using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.PayRuns;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.PayRuns;

public sealed class ReopenPayRunHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid)
    : IRequestHandler<ReopenPayRunCommand, Result>
{
    public async Task<Result> Handle(ReopenPayRunCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsReopen, cancellationToken);

        var payRun = await db.PayRuns
            .FirstOrDefaultAsync(r => r.Id == request.PayRunId, cancellationToken);

        if (payRun is null)
            return Result.Failure("Pay run not found.");

        try
        {
            var actor = currentUser.Email ?? currentUser.UserId ?? "system";
            var now = clock.UtcNowOffset;

            payRun.Reopen(actor, now, guid.NewGuid());

            // Cascade: Approved → Calculated for all non-Disputed payouts in this run.
            // Paid and Disputed payouts are NOT reverted (only Approved → Calculated).
            var approvedPayouts = await db.CompensationPayouts
                .Where(p => p.PayRunId == request.PayRunId
                         && p.Status == CompensationPayoutStatus.Approved)
                .ToListAsync(cancellationToken);

            foreach (var payout in approvedPayouts)
                payout.RevertToCalculated(actor, now);

            // Recompute roll-ups now that payouts are back to Calculated.
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
