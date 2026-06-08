using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Helpers;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Payees;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Application.Compensation.Handlers.Payees;

/// <summary>
/// Provides gauge attainment data + earnings trend for the Overview dashboard.
/// List cards (quotas, assignments, credits) have their own paginated endpoints.
/// </summary>
public sealed class GetPayeeDashboardHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IClock clock)
    : IRequestHandler<GetPayeeDashboardQuery, Result<PayeeDashboardDto>>
{
    public async Task<Result<PayeeDashboardDto>> Handle(
        GetPayeeDashboardQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.QuotasRead, cancellationToken);

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var payeeId = request.PayeeId;
        var (rangeFrom, rangeTo) = PeriodHelper.ComputeDateRange(request.Period, today);

        // ── Load quotas (period filter in-memory — owned DateRange) ───────────
        var allQuotas = await db.Quotas
            .Where(q => q.PayeeId == payeeId && q.Status != QuotaStatus.Draft)
            .OrderByDescending(q => q.Period.Start)
            .ToListAsync(cancellationToken);

        // Intersection filter: quota period [Start, End] intersects selected range [rangeFrom, rangeTo]
        var quotas = allQuotas.Where(q =>
            (!rangeFrom.HasValue || q.Period.End >= rangeFrom.Value) &&
            (!rangeTo.HasValue || q.Period.Start <= rangeTo.Value))
            .ToList();

        // ── Load all non-superseded credits for this payee ───────────────────
        // No period filter here — attainment is computed against each quota's own period boundaries.
        // TxAmount = gross transaction revenue (Sales Quota semantic, not commission).
        var allCredits = await (
            from c in db.Credits
            join t in db.CompensationTransactions on c.TransactionId equals t.Id
            where c.PayeeId == payeeId && c.SupersededAt == null
            select new
            {
                c.PlanId,
                TxAmount = t.Amount.Amount,
                Currency = t.Amount.Currency,
                t.TransactionDate
            }
        ).ToListAsync(cancellationToken);

        // ── Load plan names + currencies for quotas ───────────────────────────
        var planIds = quotas.Select(q => q.PlanId).Distinct().ToList();
        var planInfoById = planIds.Count > 0
            ? await db.CompensationPlans
                .Where(p => planIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name, p.Currency })
                .ToDictionaryAsync(p => p.Id, p => (p.Name, p.Currency), cancellationToken)
            : new Dictionary<Guid, (string Name, string Currency)>();

        // ── Card 1: Attainment gauges ─────────────────────────────────────────
        var attainmentItems = new List<QuotaAttainmentDto>(quotas.Count());
        foreach (var quota in quotas)
        {
            var start = quota.Period.Start;
            var end = quota.Period.End;
            var pId = quota.PlanId;

            var planInfo = planInfoById.GetValueOrDefault(quota.PlanId, (Name: string.Empty, Currency: string.Empty));
            var planCurrency = planInfo.Currency;
            var isCurrencyValid = string.Equals(quota.Amount.Currency, planCurrency, StringComparison.OrdinalIgnoreCase);

            decimal achieved;
            if (quota.MeasurementType == QuotaMeasurementType.Units)
            {
                achieved = await ComputeUnitsAchievedAsync(payeeId, pId, start, end, cancellationToken);
            }
            else
            {
                var quotaCurrency = quota.Amount.Currency;
                achieved = allCredits
                    .Where(r => r.PlanId == pId &&
                                r.Currency == quotaCurrency &&
                                r.TransactionDate >= start &&
                                r.TransactionDate <= end)
                    .Sum(r => r.TxAmount);
            }

            var attainment = AttainmentPercentage.FromAchievedAndTarget(achieved, quota.Amount.Amount);
            attainmentItems.Add(new QuotaAttainmentDto(
                QuotaId: quota.Id,
                PlanId: quota.PlanId,
                PlanName: planInfo.Name,
                MeasurementType: quota.MeasurementType,
                TargetAmount: quota.Amount.Amount,
                Currency: quota.Amount.Currency,
                AchievedAmount: achieved,
                AttainmentValue: attainment.Value,
                AttainmentPercent: attainment.ToPercentString(),
                PeriodStart: quota.Period.Start,
                PeriodEnd: quota.Period.End,
                Status: quota.Status.ToString(),
                IsCurrencyValid: isCurrencyValid,
                PlanCurrency: planCurrency));
        }

        // ── Card 2: Sales trend (last 12 months, not affected by period filter) ──
        // Uses Transaction.Amount (gross sales) to match the Sales Quota semantic.
        // Credit serves as the plan-routing oracle; bars represent sales generated, not commission earned.
        var trendCutoff = today.AddMonths(-12);
        var allCreditsForTrend = await (
            from c in db.Credits
            join t in db.CompensationTransactions on c.TransactionId equals t.Id
            where c.PayeeId == payeeId && c.SupersededAt == null && t.TransactionDate >= trendCutoff
            select new { Amount = t.Amount.Amount, Currency = t.Amount.Currency, t.TransactionDate }
        ).ToListAsync(cancellationToken);

        var trend = allCreditsForTrend
            .GroupBy(r => new { r.TransactionDate.Year, r.TransactionDate.Month, r.Currency })
            .Select(g => new SalesTrendPointDto(
                Year: g.Key.Year,
                Month: g.Key.Month,
                MonthLabel: new DateTime(g.Key.Year, g.Key.Month, 1)
                    .ToString("MMM", CultureInfo.InvariantCulture),
                Amount: g.Sum(r => r.Amount),
                Currency: g.Key.Currency))
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToList();

        return Result<PayeeDashboardDto>.Success(new PayeeDashboardDto(
            attainmentItems,
            trend,
            Array.Empty<QuotaSummaryDto>(),
            Array.Empty<PlanAssignmentSummaryDto>()));
    }

    private async Task<decimal> ComputeUnitsAchievedAsync(
        Guid payeeId, Guid planId, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var quantities = await (
            from c in db.Credits
            join t in db.CompensationTransactions on c.TransactionId equals t.Id
            where c.PayeeId == payeeId && c.PlanId == planId
               && c.SupersededAt == null
               && t.TransactionDate >= start && t.TransactionDate <= end
            select t.Quantity
        ).ToListAsync(ct);
        return quantities.Sum();
    }
}
