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
    private readonly Dictionary<(Guid, Guid, DateOnly), AttainmentPercentage> _cache = new();

    public QuotaAttainmentService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<AttainmentPercentage> ComputeAsync(
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

    private async Task<AttainmentPercentage> ComputeInternalAsync(
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

        if (matching.Count == 0) return AttainmentPercentage.Zero;

        // Tie-break when periods overlap: shortest span first, then most recent CreatedAt.
        var quota = matching
            .OrderBy(q => q.Period.End.DayNumber - q.Period.Start.DayNumber)
            .ThenByDescending(q => q.CreatedAt)
            .First();

        var target = quota.Amount.Amount;
        var periodStart = quota.Period.Start;
        var periodEnd = quota.Period.End;

        decimal achieved = quota.MeasurementType == QuotaMeasurementType.Units
            ? await ComputeUnitsAchievedAsync(payeeId, planId, periodStart, periodEnd, ct)
            : await ComputeRevenueAchievedAsync(payeeId, planId, periodStart, periodEnd, quota.Amount.Currency, ct);

        return AttainmentPercentage.FromAchievedAndTarget(achieved, target);
    }

    // Revenue (Sales Quota): sum Transaction.Amount of non-superseded Credits whose source
    // transaction date falls within the quota period AND whose currency matches the quota's currency.
    // Uses Transaction.Amount (gross sales) NOT CreditedAmount (commission). Industry-standard:
    // "Anna should sell €25k this month" → Achieved = sum of transaction revenues.
    // Credit serves as the plan-routing oracle; we sum t.Amount (the sale) not c.CreditedAmount (the commission).
    private async Task<decimal> ComputeRevenueAchievedAsync(
        Guid payeeId,
        Guid planId,
        DateOnly periodStart,
        DateOnly periodEnd,
        string quotaCurrency,
        CancellationToken ct)
    {
        var amounts = await (
            from c in _db.Credits
            join t in _db.CompensationTransactions on c.TransactionId equals t.Id
            where c.PayeeId == payeeId
               && c.PlanId == planId
               && c.SupersededAt == null
               && t.Amount.Currency == quotaCurrency
               && t.TransactionDate >= periodStart
               && t.TransactionDate <= periodEnd
            select t.Amount.Amount
        ).ToListAsync(ct);

        return amounts.Sum();
    }

    // Units: sum Quantity from source transactions of non-superseded Credits.
    private async Task<decimal> ComputeUnitsAchievedAsync(
        Guid payeeId,
        Guid planId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken ct)
    {
        var quantities = await (
            from c in _db.Credits
            join t in _db.CompensationTransactions on c.TransactionId equals t.Id
            where c.PayeeId == payeeId
               && c.PlanId == planId
               && c.SupersededAt == null
               && t.TransactionDate >= periodStart
               && t.TransactionDate <= periodEnd
            select t.Quantity
        ).ToListAsync(ct);

        return quantities.Sum();
    }
}
