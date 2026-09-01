using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Infrastructure.Compensation.Calculation;

/// <summary>
/// Scoped per request. Caches (payeeId, planId, asOfDate) results so that batch processing
/// of many transactions for the same payee+plan period only hits the DB once.
/// Relies on the global EF query filter for tenant isolation (same as FieldRequirementService).
/// </summary>
public sealed class QuotaAttainmentService : IQuotaAttainmentService
{
    private readonly IApplicationDbContext _db;
    private readonly Dictionary<(Guid, Guid, DateOnly), AttainmentReading> _cache = new();

    public QuotaAttainmentService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<AttainmentReading> ComputeAsync(
        Guid payeeId,
        Guid planId,
        DateOnly asOfDate,
        CancellationToken ct = default)
    {
        var key = (payeeId, planId, asOfDate);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var result = await ComputeInternalAsync(payeeId, planId, asOfDate, ct);
        _cache[key] = result;
        return result;
    }

    private async Task<AttainmentReading> ComputeInternalAsync(
        Guid payeeId,
        Guid planId,
        DateOnly asOfDate,
        CancellationToken ct)
    {
        // Load all non-Draft quotas for this payee+plan.
        // Period filtering is done in memory because EF Core 8 does not reliably translate
        // DateOnly comparisons on owned DateRange (Period.Start/End) in SQL WHERE clauses.
        // A payee typically has very few quotas per plan so the in-memory approach is safe.
        var quotas = await _db.Quotas
            .Where(q =>
                q.PayeeId == payeeId &&
                q.PlanId == planId &&
                q.Status != QuotaStatus.Draft)
            .ToListAsync(ct);

        var matching = quotas
            .Where(q => q.Period.Start <= asOfDate && q.Period.End >= asOfDate)
            .ToList();

        // ★ NO QUOTA IN EFFECT IS NOT 0% ATTAINMENT. It used to return a bare zero, which the engine
        // then sealed as a measured fact — a breakdown could state that somebody achieved 0% of a
        // target nobody had set. The ratio stays 0 because there is nothing else it could be; what
        // changes is that the reason travels with it.
        if (matching.Count == 0)
            return new AttainmentReading(AttainmentPercentage.Zero, AttainmentSource.NoTarget);

        // Tie-break when periods overlap: shortest span first, then most recent CreatedAt.
        var quota = matching
            .OrderBy(q => q.Period.End.DayNumber - q.Period.Start.DayNumber)
            .ThenByDescending(q => q.CreatedAt)
            .First();

        var target = quota.Amount.Amount;

        // The second way to have nothing to measure against: a quota that exists but targets zero.
        // AttainmentPercentage.FromAchievedAndTarget also returns Zero for this (AttainmentPercentage.cs:24),
        // so checking here is what keeps the two zeroes distinguishable — from outside that method
        // they are the same value.
        if (target <= 0m)
            return new AttainmentReading(AttainmentPercentage.Zero, AttainmentSource.NoTarget);

        var periodStart = quota.Period.Start;
        var periodEnd = quota.Period.End;

        decimal achieved = quota.MeasurementType == QuotaMeasurementType.Units
            ? await ComputeUnitsAchievedAsync(payeeId, planId, periodStart, periodEnd, ct)
            : await ComputeRevenueAchievedAsync(payeeId, planId, periodStart, periodEnd, quota.Amount.Currency, ct);

        // Anything that gets here was measured against a real target — including a genuine 0%, which
        // is a fact about the rep and must NOT be reported as a missing quota.
        return new AttainmentReading(
            AttainmentPercentage.FromAchievedAndTarget(achieved, target),
            AttainmentSource.Measured);
    }

    // Revenue (Sales Quota): distinct-sale sum via the shared QuotaAchievedQuery — the ONE definition of
    // "achieved" that the motor and every card share, so they cannot drift apart again.
    private Task<decimal> ComputeRevenueAchievedAsync(
        Guid payeeId,
        Guid planId,
        DateOnly periodStart,
        DateOnly periodEnd,
        string quotaCurrency,
        CancellationToken ct)
        => QuotaAchievedQuery.RevenueAsync(_db, payeeId, planId, periodStart, periodEnd, quotaCurrency, ct);

    public async Task<AttainmentSplitContext?> GetSplitContextAsync(
        Guid payeeId,
        Guid planId,
        DateOnly asOfDate,
        CancellationToken ct = default)
    {
        // No caching: PriorCumulative changes after each transaction is committed to DB.
        var quotas = await _db.Quotas
            .Where(q =>
                q.PayeeId == payeeId &&
                q.PlanId == planId &&
                q.Status != QuotaStatus.Draft)
            .ToListAsync(ct);

        var matching = quotas
            .Where(q => q.Period.Start <= asOfDate && q.Period.End >= asOfDate)
            .ToList();

        if (matching.Count == 0) return null;

        var quota = matching
            .OrderBy(q => q.Period.End.DayNumber - q.Period.Start.DayNumber)
            .ThenByDescending(q => q.CreatedAt)
            .First();

        // Units-based quotas are not supported for split-at-quota: the tier Rate is a
        // monetary percentage applied to transaction amount, not a per-unit amount.
        if (quota.MeasurementType == QuotaMeasurementType.Units) return null;

        var target = quota.Amount.Amount;
        var prior = await ComputeRevenueAchievedAsync(
            payeeId, planId, quota.Period.Start, quota.Period.End, quota.Amount.Currency, ct);

        return new AttainmentSplitContext(prior, target);
    }

    // Units: distinct-sale quantity sum via the shared QuotaAchievedQuery (same source of truth).
    private Task<decimal> ComputeUnitsAchievedAsync(
        Guid payeeId,
        Guid planId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken ct)
        => QuotaAchievedQuery.UnitsAsync(_db, payeeId, planId, periodStart, periodEnd, ct);
}
