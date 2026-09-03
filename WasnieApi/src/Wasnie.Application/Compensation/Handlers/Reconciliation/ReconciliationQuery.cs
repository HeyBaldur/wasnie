using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Reconciliation;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;

namespace Wasnie.Application.Compensation.Handlers.Reconciliation;

/// <summary>
/// One row of the union, before names are attached. A "seed" is a single (entity, reason) pairing —
/// an entity that fails for two reasons yields two seeds and is folded back into one row later.
/// </summary>
internal sealed record ReconciliationSeed
{
    public required int Kind { get; init; }
    public required Guid EntityId { get; init; }
    public required string Reason { get; init; }
    public Guid? PayeeId { get; init; }
    public Guid? PlanId { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public required int MoneyKind { get; init; }
    public DateOnly? PeriodDate { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}

/// <summary>
/// The reconciliation queue as ONE composed <c>IQueryable</c> over every source of "this money could
/// not be paid".
///
/// ★★ IT READS THE EXISTING SOURCES, IT DOES NOT RE-IMPLEMENT THEM. The three unprocessable reasons
/// come from <see cref="UnprocessablePendingSpec"/> — the same queryables the dashboard card counts
/// and the Transactions list filters by — and ambiguity from <see cref="AmbiguousAttributionSpec"/>.
/// The engine's own refusals come from <c>Credits.RateRefusal</c>, the column KAN-28 tanda A added
/// beside the trace. A second definition of "unpayable" living here is exactly the count-versus-list
/// drift this screen was asked not to create.
///
/// ★★ EVERYTHING IS AN IQueryable UNTIL THE VERY END. The seeds are concatenated, filtered, counted,
/// grouped and paged in SQL. That is not a performance preference: the cards have to be the SAME
/// query as the rows, and the only way a total can be guaranteed to match a table is if neither was
/// assembled in memory from a page.
/// </summary>
internal static class ReconciliationQuery
{
    internal const int KindCredit = (int)ReconciliationEntryKind.Credit;
    internal const int KindTransaction = (int)ReconciliationEntryKind.Transaction;
    internal const int KindPlan = (int)ReconciliationEntryKind.Plan;

    internal const int MoneyNone = (int)ReconciliationMoneyKind.None;
    internal const int MoneyBase = (int)ReconciliationMoneyKind.AffectedBase;
    internal const int MoneyClawback = (int)ReconciliationMoneyKind.Clawback;

    /// <summary>
    /// Every seed, unfiltered.
    ///
    /// ★ THE CONCAT ORDER IS THE READING ORDER OF THE TICKET'S TABLE, and it carries no meaning
    /// beyond that — ordering happens at the end, over the whole union.
    /// </summary>
    internal static IQueryable<ReconciliationSeed> Seeds(IApplicationDbContext db)
    {
        // ── The engine's own refusals, via the queryable column beside the trace ─────────────
        //
        // ★ SupersededAt == null: a superseded credit was replaced by a later calculation, so its
        // refusal is history rather than an open item. Consumed credits cannot appear here at all —
        // a refused credit is worth zero and never reaches a payout.
        var refusedCredits =
            from c in db.Credits
            where c.SupersededAt == null && c.RateRefusal != null
            join t in db.CompensationTransactions on c.TransactionId equals t.Id
            select new ReconciliationSeed
            {
                Kind = KindCredit,
                EntityId = c.Id,
                Reason = c.RateRefusal!,
                PayeeId = c.PayeeId,
                PlanId = c.PlanId,
                // The SALE, not a commission: when the engine refuses, the commission is the number
                // nobody knows. See ReconciliationMoneyKind.AffectedBase.
                Amount = c.OriginalAmount.Amount,
                Currency = c.OriginalAmount.Currency,
                MoneyKind = MoneyBase,
                PeriodDate = t.TransactionDate,
                OccurredAt = c.AllocatedAt,
            };

        // ── The three unprocessable-pending reasons, from the shared spec ────────────────────
        var noPayee = Pending(UnprocessablePendingSpec.NoPayee(db), ReconciliationReason.NoPayee);
        var currencyMismatch = Pending(UnprocessablePendingSpec.CurrencyMismatch(db), ReconciliationReason.CurrencyMismatch);
        var noAssignment = Pending(UnprocessablePendingSpec.NoActiveAssignment(db), ReconciliationReason.NoActiveAssignment);
        var ambiguous = Pending(AmbiguousAttributionSpec.Queryable(db), ReconciliationReason.AmbiguousAttribution);

        // ── A sale that is missing nothing and still carries no credit (KAN-50) ──────────────
        //
        // ★ THE HOLE THE OTHER FOUR LEFT. The three unprocessable reasons name what a transaction
        // LACKS, and ambiguity names a choice nobody made. A Pending transaction that lacks nothing
        // and had no choice to make fell through all four, so the queue — the screen whose promise is
        // that unpaid money is visible — was silently complete-looking while such rows existed. It
        // carries the sale, like the other Pending reasons, for the same reason: the commission is
        // the number nobody knows.
        var processableWithoutCredit = Pending(
            ProcessableWithoutCreditSpec.Queryable(db), ReconciliationReason.ProcessableWithoutCredit);

        // ── A deal that left closed-won after its commission was calculated or paid ──────────
        //
        // ★ THE CLAWBACK POT, AND IT NEVER JOINS THE OTHER ONE. This is money already paid out that
        // has to come back; adding it to money still owed would produce a net that means nothing.
        var dealLost =
            from a in db.DealLostAlerts
            where a.ResolvedAt == null
            join t in db.CompensationTransactions on a.TransactionId equals t.Id
            select new ReconciliationSeed
            {
                Kind = KindTransaction,
                EntityId = a.TransactionId,
                Reason = ReconciliationReason.DealLost,
                PayeeId = t.PayeeId,
                PlanId = null,
                Amount = a.CommissionAmount,
                Currency = a.CommissionCurrency,
                MoneyKind = MoneyClawback,
                PeriodDate = t.TransactionDate,
                OccurredAt = a.DetectedAt,
            };

        // ── A deal that CHANGED in the CRM after its commission was calculated or paid ───────
        //
        // ★ NO AMOUNT ON PURPOSE. The exposure is the difference between two figures the alert
        // records, and whether that difference is owed, owed back, or neither depends on a
        // recalculation nobody has run. Publishing a number here would be a guess wearing a total's
        // clothes, so this row is counted and shown and contributes nothing to the money cards.
        var drift =
            from a in db.CrmDriftAlerts
            where a.ResolvedAt == null
            join t in db.CompensationTransactions on a.TransactionId equals t.Id
            select new ReconciliationSeed
            {
                Kind = KindTransaction,
                EntityId = a.TransactionId,
                Reason = ReconciliationReason.CrmDrift,
                PayeeId = t.PayeeId,
                PlanId = null,
                Amount = null,
                Currency = null,
                MoneyKind = MoneyNone,
                PeriodDate = t.TransactionDate,
                OccurredAt = a.DetectedAt,
            };

        // ── An Active plan whose every rule is stopped ───────────────────────────────────────
        //
        // ★ THE SAME PREDICATE THE DASHBOARD CARD USES, and no amount: a plan is a CAUSE, not a sum.
        // It has no payee and no sale behind it, so it appears in the list and in the count by
        // reason, and never in a currency total.
        var deadPlans =
            from p in PlansWithoutLiveRulesSpec.Queryable(db)
            select new ReconciliationSeed
            {
                Kind = KindPlan,
                EntityId = p.Id,
                Reason = ReconciliationReason.PlanHasNoActiveRules,
                PayeeId = null,
                PlanId = p.Id,
                Amount = null,
                Currency = null,
                MoneyKind = MoneyNone,
                PeriodDate = null,
                OccurredAt = p.UpdatedAt,
            };

        return refusedCredits
            .Concat(noPayee)
            .Concat(currencyMismatch)
            .Concat(noAssignment)
            .Concat(ambiguous)
            .Concat(processableWithoutCredit)
            .Concat(dealLost)
            .Concat(drift)
            .Concat(deadPlans);

        IQueryable<ReconciliationSeed> Pending(
            IQueryable<Domain.Compensation.Transactions.CompensationTransaction> source, string reason) =>
            source.Select(t => new ReconciliationSeed
            {
                Kind = KindTransaction,
                EntityId = t.Id,
                Reason = reason,
                PayeeId = t.PayeeId,
                PlanId = null,
                Amount = t.Amount.Amount,
                Currency = t.Amount.Currency,
                MoneyKind = MoneyBase,
                PeriodDate = t.TransactionDate,
                OccurredAt = t.IngestedAt,
            });
    }

    /// <summary>
    /// The filtered seeds.
    ///
    /// ★★ THE REASON FILTER IS APPLIED TO THE ENTITY, NOT TO THE SEED, and that distinction is the
    /// whole "an entry with two reasons appears once, with BOTH" rule. Filtering seeds directly would
    /// return a row stripped of its other reason — the screen would then say a transaction failed for
    /// one thing when it failed for two. So the reason narrows WHICH entities qualify, and every seed
    /// of a qualifying entity comes back.
    /// </summary>
    internal static IQueryable<ReconciliationSeed> Filtered(
        IApplicationDbContext db, ReconciliationFilter filter)
    {
        var seeds = Seeds(db);

        if (filter.PayeeId.HasValue)
            seeds = seeds.Where(s => s.PayeeId == filter.PayeeId.Value);

        if (filter.From.HasValue)
            seeds = seeds.Where(s => s.PeriodDate != null && s.PeriodDate >= filter.From.Value);

        if (filter.To.HasValue)
            seeds = seeds.Where(s => s.PeriodDate != null && s.PeriodDate <= filter.To.Value);

        if (!string.IsNullOrWhiteSpace(filter.Reason))
        {
            var reason = filter.Reason;
            var matching = seeds.Where(s => s.Reason == reason)
                .Select(s => new { s.Kind, s.EntityId });

            seeds = seeds.Where(s => matching.Any(m => m.Kind == s.Kind && m.EntityId == s.EntityId));
        }

        return seeds;
    }

    /// <summary>
    /// The aggregates, over the WHOLE filtered set, in SQL.
    ///
    /// ★★ MONEY IS SUMMED OVER DISTINCT ENTITIES, NOT OVER SEEDS. An entity that appears under two
    /// reasons must contribute its amount ONCE — that is the "sin contarse dos veces" rule, and
    /// getting it wrong would inflate the very number the screen exists to state precisely. The
    /// Distinct() below is on the money-bearing tuple, so a two-reason entity collapses to one.
    /// <c>ReconciliationDoubleCountTests</c> pins it.
    /// </summary>
    internal static async Task<ReconciliationSummaryDto> SummariseAsync(
        IApplicationDbContext db, ReconciliationFilter filter, CancellationToken ct)
    {
        var seeds = Filtered(db, filter);

        var totalRows = await seeds
            .Select(s => new { s.Kind, s.EntityId })
            .Distinct()
            .CountAsync(ct);

        // One tuple per entity that carries money. Distinct collapses the duplicate seeds a
        // multi-reason entity produces; the reason is deliberately NOT part of the tuple.
        var moneyRows = await seeds
            .Where(s => s.MoneyKind != MoneyNone && s.Currency != null && s.Amount != null)
            .Select(s => new { s.Kind, s.EntityId, s.Currency, s.MoneyKind, s.Amount })
            .Distinct()
            .ToListAsync(ct);

        var byCurrency = moneyRows
            .GroupBy(r => r.Currency!)
            .Select(g => new ReconciliationCurrencyTotalDto(
                Currency: g.Key,
                AffectedBaseAmount: g.Where(x => x.MoneyKind == MoneyBase).Sum(x => x.Amount!.Value),
                ClawbackAmount: g.Where(x => x.MoneyKind == MoneyClawback).Sum(x => x.Amount!.Value),
                RowCount: g.Select(x => new { x.Kind, x.EntityId }).Distinct().Count()))
            .OrderBy(t => t.Currency)
            .ToList();

        // A reason's count is a count of ENTITIES, so a two-reason entity appears under both and is
        // still one row in TotalRows.
        var byReason = await seeds
            .Select(s => new { s.Reason, s.Kind, s.EntityId })
            .Distinct()
            .GroupBy(s => s.Reason)
            .Select(g => new ReconciliationReasonCountDto(g.Key, g.Count()))
            .ToListAsync(ct);

        return new ReconciliationSummaryDto(
            TotalRows: totalRows,
            ByCurrency: byCurrency,
            ByReason: byReason.OrderByDescending(r => r.Count).ThenBy(r => r.Reason).ToList());
    }
}
