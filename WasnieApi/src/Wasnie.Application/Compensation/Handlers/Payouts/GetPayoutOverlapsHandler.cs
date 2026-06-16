using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Payouts;

public sealed class GetPayoutOverlapsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetPayoutOverlapsQuery, Result<IReadOnlyList<OverlappingPayoutDto>>>
{
    public async Task<Result<IReadOnlyList<OverlappingPayoutDto>>> Handle(
        GetPayoutOverlapsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsRead, cancellationToken);

        var payout = await db.CompensationPayouts
            .AsNoTracking()
            .Where(p => p.Id == request.PayoutId)
            .Select(p => new { p.Id, p.PayeeId, PeriodStart = p.Period.Start, PeriodEnd = p.Period.End })
            .FirstOrDefaultAsync(cancellationToken);

        if (payout is null)
            return Result<IReadOnlyList<OverlappingPayoutDto>>.Failure("Payout not found.");

        var payeeId    = payout.PayeeId;
        var periodStart = payout.PeriodStart;
        var periodEnd   = payout.PeriodEnd;

        // [a1,a2] and [b1,b2] overlap iff a1 <= b2 AND b1 <= a2 (inclusive endpoints).
        var overlapping = await db.CompensationPayouts
            .AsNoTracking()
            .Where(p => p.Id != request.PayoutId
                     && p.PayeeId == payeeId
                     && (p.Status == CompensationPayoutStatus.Approved || p.Status == CompensationPayoutStatus.Paid)
                     && p.Period.Start <= periodEnd
                     && p.Period.End >= periodStart)
            .OrderByDescending(p => p.Period.Start)
            .Select(p => new
            {
                p.Id,
                PeriodStart = p.Period.Start,
                PeriodEnd   = p.Period.End,
                p.Status,
                p.PlanId,
                Amount   = p.TotalCommission.Amount,
                Currency = p.TotalCommission.Currency,
            })
            .ToListAsync(cancellationToken);

        if (overlapping.Count == 0)
            return Result<IReadOnlyList<OverlappingPayoutDto>>.Success([]);

        var planIds = overlapping.Select(p => p.PlanId).Distinct().ToList();
        var planLookup = await db.CompensationPlans
            .Where(p => planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        var dtos = overlapping
            .Select(p => new OverlappingPayoutDto(
                p.Id,
                p.PeriodStart,
                p.PeriodEnd,
                p.Status.ToString(),
                planLookup.TryGetValue(p.PlanId, out var name) ? name : p.PlanId.ToString("N")[..8],
                p.Amount,
                p.Currency))
            .ToList();

        return Result<IReadOnlyList<OverlappingPayoutDto>>.Success(dtos);
    }
}
