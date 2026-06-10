using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Payouts;
using Wasnie.Application.Compensation.Queries.PayRuns;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.PayRuns;

public sealed class GetPayRunByIdHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetPayRunByIdQuery, Result<PayRunDetailDto>>
{
    public async Task<Result<PayRunDetailDto>> Handle(
        GetPayRunByIdQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsRead, cancellationToken);

        var payRun = await db.PayRuns
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (payRun is null)
            return Result<PayRunDetailDto>.Failure("Pay run not found.");

        // Load payouts for this run with optional zero-exclusion.
        var f = request.PayoutsFilter;
        var payoutsQuery = db.CompensationPayouts
            .Where(p => p.PayRunId == request.Id
                     && p.Status != CompensationPayoutStatus.Disputed);

        if (f.ExcludeZero)
            payoutsQuery = payoutsQuery.Where(p => p.TotalCommission.Amount > 0);

        payoutsQuery = payoutsQuery.OrderByDescending(p => p.TotalCommission.Amount);

        var totalCount = await payoutsQuery.CountAsync(cancellationToken);
        var payoutPage = await payoutsQuery
            .Skip((f.Page - 1) * f.PageSize)
            .Take(f.PageSize)
            .ToListAsync(cancellationToken);

        // Batch-load plan names for payouts on this page.
        var planIds = payoutPage.Select(p => p.PlanId).Distinct().ToList();
        var planLookup = await db.CompensationPlans
            .Where(p => planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var payoutDtos = payoutPage.Select(p =>
        {
            planLookup.TryGetValue(p.PlanId, out var plan);
            return new PayoutListItemDto(
                Id: p.Id,
                PayeeId: p.PayeeId,
                PayeeName: p.PayeeSnapshot.FullName,
                PayeeCode: p.PayeeSnapshot.EmployeeCode,
                PlanId: p.PlanId,
                PlanName: plan?.Name ?? p.PlanId.ToString("N")[..8],
                PeriodStart: p.Period.Start,
                PeriodEnd: p.Period.End,
                TotalCommissionAmount: p.TotalCommission.Amount,
                TotalCommissionCurrency: p.TotalCommission.Currency,
                Status: p.Status.ToString(),
                CalculatedAt: p.CalculatedAt,
                CalculatedBy: p.CalculatedBy,
                UpdatedAt: p.UpdatedAt,
                UpdatedBy: p.UpdatedBy);
        }).ToList();

        var dto = new PayRunDetailDto(
            Id: payRun.Id,
            PeriodStart: payRun.PeriodStart,
            PeriodEnd: payRun.PeriodEnd,
            Status: payRun.Status.ToString(),
            PayeeCount: payRun.PayeeCount,
            PaidPayeeCount: payRun.PaidPayeeCount,
            ZeroPayoutCount: payRun.ZeroPayoutCount,
            TotalAmounts: payRun.TotalAmounts,
            CreatedAt: payRun.CreatedAt,
            CreatedBy: payRun.CreatedBy,
            ApprovedAt: payRun.ApprovedAt,
            ApprovedBy: payRun.ApprovedBy,
            PaidAt: payRun.PaidAt,
            PaidBy: payRun.PaidBy,
            Payouts: new PagedResult<PayoutListItemDto>
            {
                Items = payoutDtos,
                TotalCount = totalCount,
                Page = f.Page,
                PageSize = f.PageSize,
            });

        return Result<PayRunDetailDto>.Success(dto);
    }
}
