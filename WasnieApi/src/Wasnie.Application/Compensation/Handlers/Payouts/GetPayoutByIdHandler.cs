using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Payouts;

public sealed class GetPayoutByIdHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetPayoutByIdQuery, Result<PayoutDto>>
{
    public async Task<Result<PayoutDto>> Handle(
        GetPayoutByIdQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsRead, cancellationToken);

        var payout = await db.CompensationPayouts
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (payout is null)
            return Result<PayoutDto>.Failure("Payout not found.");

        var lines = payout.Lines.Select(l => new PayoutLineDto(
            Id: l.Id,
            CreditId: l.CreditId,
            RuleId: l.RuleId,
            RuleName: l.RuleName,
            BaseAmount: l.BaseAmount.Amount,
            BaseCurrency: l.BaseAmount.Currency,
            CommissionAmount: l.CommissionAmount.Amount,
            CommissionCurrency: l.CommissionAmount.Currency)).ToList();

        var dto = new PayoutDto(
            Id: payout.Id,
            TenantId: payout.TenantId,
            PayeeId: payout.PayeeId,
            PayeeName: payout.PayeeSnapshot.FullName,
            PayeeCode: payout.PayeeSnapshot.EmployeeCode,
            PlanId: payout.PlanId,
            PeriodStart: payout.Period.Start,
            PeriodEnd: payout.Period.End,
            TotalCommissionAmount: payout.TotalCommission.Amount,
            TotalCommissionCurrency: payout.TotalCommission.Currency,
            Status: payout.Status.ToString(),
            CalculatedAt: payout.CalculatedAt,
            CalculatedBy: payout.CalculatedBy,
            UpdatedAt: payout.UpdatedAt,
            UpdatedBy: payout.UpdatedBy,
            Lines: lines);

        return Result<PayoutDto>.Success(dto);
    }
}
