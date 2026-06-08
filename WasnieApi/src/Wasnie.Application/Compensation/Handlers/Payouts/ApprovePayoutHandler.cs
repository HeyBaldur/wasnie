using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Payouts;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Payouts;

public sealed class ApprovePayoutHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid)
    : IRequestHandler<ApprovePayoutCommand, Result>
{
    public async Task<Result> Handle(ApprovePayoutCommand request, CancellationToken cancellationToken)
    {
        var payout = await db.CompensationPayouts
            .FirstOrDefaultAsync(p => p.Id == request.PayoutId, cancellationToken);

        if (payout is null)
            return Result.Failure("Payout not found.");

        try
        {
            payout.Approve(
                currentUser.UserId ?? "system",
                clock.UtcNowOffset,
                guid.NewGuid());

            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
