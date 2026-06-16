using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Payouts;

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

        var planName = await db.CompensationPlans
            .Where(p => p.Id == payout.PlanId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var lines = await BuildLinesAsync(payout.Lines, db, cancellationToken);

        var dto = new PayoutDto(
            Id: payout.Id,
            TenantId: payout.TenantId,
            PayeeId: payout.PayeeId,
            PayeeName: payout.PayeeSnapshot.FullName,
            PayeeCode: payout.PayeeSnapshot.EmployeeCode,
            PlanId: payout.PlanId,
            PlanName: planName,
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

    // Resolves source transactions for all lines in two bulk queries (no N+1).
    public static async Task<List<PayoutLineDto>> BuildLinesAsync(
        IReadOnlyList<PayoutLine> lines,
        IApplicationDbContext db,
        CancellationToken ct)
    {
        var creditIds = lines.Select(l => l.CreditId).Distinct().ToList();

        var creditTxMap = await db.Credits
            .Where(c => creditIds.Contains(c.Id))
            .Select(c => new { c.Id, c.TransactionId })
            .ToDictionaryAsync(c => c.Id, c => c.TransactionId, ct);

        var transactionIds = creditTxMap.Values.Distinct().ToList();

        var txById = await db.CompensationTransactions
            .Where(t => transactionIds.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.ReferenceNumber,
                t.ExternalId,
                t.TransactionDate,
                AmountValue = t.Amount.Amount,
                AmountCurrency = t.Amount.Currency,
            })
            .ToDictionaryAsync(t => t.Id, ct);

        return lines.Select(l =>
        {
            creditTxMap.TryGetValue(l.CreditId, out var txId);
            var tx = txId != default ? txById.GetValueOrDefault(txId) : null;

            return new PayoutLineDto(
                Id: l.Id,
                CreditId: l.CreditId,
                RuleId: l.RuleId,
                RuleName: l.RuleName,
                BaseAmount: l.BaseAmount.Amount,
                BaseCurrency: l.BaseAmount.Currency,
                CommissionAmount: l.CommissionAmount.Amount,
                CommissionCurrency: l.CommissionAmount.Currency,
                TransactionId: tx?.Id,
                TransactionReference: tx?.ReferenceNumber,
                TransactionExternalId: tx?.ExternalId,
                TransactionDate: tx?.TransactionDate,
                TransactionAmount: tx?.AmountValue,
                TransactionCurrency: tx?.AmountCurrency);
        }).ToList();
    }
}
