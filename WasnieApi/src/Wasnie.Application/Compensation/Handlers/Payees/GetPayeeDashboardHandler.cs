using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
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
        var isActive = !string.Equals(request.Period, "all", StringComparison.OrdinalIgnoreCase);

        // ── Load quotas (period filter in-memory — owned DateRange) ───────────
        var allQuotas = await db.Quotas
            .Where(q => q.PayeeId == payeeId && q.Status != QuotaStatus.Draft)
            .OrderByDescending(q => q.Period.Start)
            .ToListAsync(cancellationToken);

        var quotas = isActive
            ? allQuotas.Where(q => q.Period.End >= today).ToList()
            : allQuotas;

        // ── Load all non-superseded credits for this payee ───────────────────
        var cutoff = isActive ? today.AddDays(-90) : (DateOnly?)null;
        var allCredits = await (
            from c in db.Credits
            join t in db.CompensationTransactions on c.TransactionId equals t.Id
            where c.PayeeId == payeeId && c.SupersededAt == null
            select new
            {
                c.PlanId,
                c.OriginalAmount.Amount,
                c.OriginalAmount.Currency,
                t.TransactionDate
            }
        ).ToListAsync(cancellationToken);

        if (cutoff.HasValue)
            allCredits = allCredits.Where(r => r.TransactionDate >= cutoff.Value).ToList();

        // ── Load plan names for quotas ────────────────────────────────────────
        var planIds = quotas.Select(q => q.PlanId).Distinct().ToList();
        var planNameById = planIds.Count > 0
            ? await db.CompensationPlans
                .Where(p => planIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name })
                .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        // ── Card 1: Attainment gauges ─────────────────────────────────────────
        var attainmentItems = new List<QuotaAttainmentDto>(quotas.Count());
        foreach (var quota in quotas)
        {
            var start = quota.Period.Start;
            var end = quota.Period.End;
            var pId = quota.PlanId;

            decimal achieved;
            if (quota.MeasurementType == QuotaMeasurementType.Units)
            {
                achieved = await ComputeUnitsAchievedAsync(payeeId, pId, start, end, cancellationToken);
            }
            else
            {
                achieved = allCredits
                    .Where(r => r.PlanId == pId &&
                                r.TransactionDate >= start &&
                                r.TransactionDate <= end)
                    .Sum(r => r.Amount);
            }

            var attainment = AttainmentPercentage.FromAchievedAndTarget(achieved, quota.Amount.Amount);
            attainmentItems.Add(new QuotaAttainmentDto(
                QuotaId: quota.Id,
                PlanId: quota.PlanId,
                PlanName: planNameById.GetValueOrDefault(quota.PlanId, string.Empty),
                MeasurementType: quota.MeasurementType,
                TargetAmount: quota.Amount.Amount,
                Currency: quota.Amount.Currency,
                AchievedAmount: achieved,
                AttainmentValue: attainment.Value,
                AttainmentPercent: attainment.ToPercentString(),
                PeriodStart: quota.Period.Start,
                PeriodEnd: quota.Period.End,
                Status: quota.Status.ToString()));
        }

        // ── Card 2: Earnings trend (last 12 months, not affected by period filter) ─
        var trendCutoff = today.AddMonths(-12);
        var allCreditsForTrend = await (
            from c in db.Credits
            join t in db.CompensationTransactions on c.TransactionId equals t.Id
            where c.PayeeId == payeeId && c.SupersededAt == null && t.TransactionDate >= trendCutoff
            select new { c.OriginalAmount.Amount, c.OriginalAmount.Currency, t.TransactionDate }
        ).ToListAsync(cancellationToken);

        var trend = allCreditsForTrend
            .GroupBy(r => new { r.TransactionDate.Year, r.TransactionDate.Month, r.Currency })
            .Select(g => new EarningsTrendPointDto(
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
            Array.Empty<QuotaSummaryDto>(),       // lists served by separate paginated endpoints
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
