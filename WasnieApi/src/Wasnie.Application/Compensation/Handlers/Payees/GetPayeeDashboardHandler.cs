using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Helpers;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
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

            // Deduped achieved via the shared QuotaAchievedQuery — the SAME number the motor computes, so
            // the card can't drift back to the per-credit double count (the 671%-vs-336% bug). One EXISTS
            // query per quota; a payee has few quotas, so no meaningful N+1.
            var achieved = quota.MeasurementType == QuotaMeasurementType.Units
                ? await QuotaAchievedQuery.UnitsAsync(db, payeeId, pId, start, end, cancellationToken)
                : await QuotaAchievedQuery.RevenueAsync(db, payeeId, pId, start, end, quota.Amount.Currency, cancellationToken);

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
        // Uses Transaction.Amount (gross sales) to match the Sales Quota semantic. Bars represent sales
        // generated, not commission earned. Same dedup as attainment: a sale credited by several rules is
        // still ONE sale, so we query the distinct transactions (EXISTS over the payee's live credits)
        // rather than joining Credits→Tx, which would double a multi-rule sale in the bars too.
        var trendCutoff = today.AddMonths(-12);
        var salesForTrend = await db.CompensationTransactions
            .Where(t => t.TransactionDate >= trendCutoff &&
                        db.Credits.Any(c => c.TransactionId == t.Id && c.PayeeId == payeeId && c.SupersededAt == null))
            .Select(t => new { Amount = t.Amount.Amount, Currency = t.Amount.Currency, t.TransactionDate })
            .ToListAsync(cancellationToken);

        var trend = salesForTrend
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
}
