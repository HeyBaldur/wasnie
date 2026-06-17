using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Payouts;

public sealed class CheckPayoutsOverlapsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<CheckPayoutsOverlapsQuery, Result<int>>
{
    public async Task<Result<int>> Handle(
        CheckPayoutsOverlapsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsRead, cancellationToken);

        if (request.PayoutIds.Count == 0)
            return Result<int>.Success(0);

        var payouts = await db.CompensationPayouts
            .AsNoTracking()
            .Where(p => request.PayoutIds.Contains(p.Id))
            .Select(p => new { p.Id, p.PayeeId, PeriodStart = p.Period.Start, PeriodEnd = p.Period.End })
            .ToListAsync(cancellationToken);

        var count = 0;
        foreach (var p in payouts)
        {
            var id          = p.Id;
            var payeeId     = p.PayeeId;
            var periodStart = p.PeriodStart;
            var periodEnd   = p.PeriodEnd;

            var hasOverlap = await db.CompensationPayouts
                .AnyAsync(other =>
                    other.PayeeId == payeeId &&
                    other.Id != id &&
                    (other.Status == CompensationPayoutStatus.Approved ||
                     other.Status == CompensationPayoutStatus.Paid) &&
                    other.Period.Start <= periodEnd &&
                    other.Period.End >= periodStart,
                    cancellationToken);

            if (hasOverlap) count++;
        }

        return Result<int>.Success(count);
    }
}
