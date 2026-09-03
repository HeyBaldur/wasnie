using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Transactions;

namespace Wasnie.Application.Compensation.Common;

/// <summary>
/// "This transaction's plan cannot be determined." A transaction is AMBIGUOUS when nobody declared a
/// plan for it and its payee has more than one eligible assignment — the engine would otherwise pick
/// one by an arbitrary tie-break (shortest period, then smallest Id) and silently decide how much
/// commission gets paid. That is the bug this guard exists to stop.
///
/// Wasnie plans carry no eligibility criteria beyond payee + period + currency, so when two match the
/// engine genuinely has nothing to decide with. Until plans can express what they apply to, refusing
/// to guess is the honest behaviour: the transaction stays Pending and visible, untouched.
///
/// Eligibility is delegated to <see cref="PlanAssignmentResolver.Candidates"/> — the engine's own rule.
/// Re-deriving it here would let the dashboard claim an ambiguity the engine does not see, or miss one
/// it does.
/// </summary>
public static class AmbiguousAttributionSpec
{
    public const string Reason = "AmbiguousAttribution";

    /// <summary>
    /// Human-readable skip reason recorded by the processing job, so the transaction's blockage is
    /// explained where the other skip reasons already appear.
    /// </summary>
    public static string SkipReason(int candidateCount) =>
        $"Ambiguous plan attribution: this payee has {candidateCount} eligible plans for this transaction " +
        "and none was chosen, so the plan cannot be determined. Review the payee's assignments.";

    /// <summary>
    /// Scalar overload so the dashboard can evaluate thousands of Pending rows from a lightweight
    /// projection instead of materialising full entities. All data must already be loaded — this makes
    /// no DB calls, so it runs inside the batch job loop and the dashboard's in-memory matching alike.
    /// </summary>
    /// <returns>The eligible assignments when ambiguous (2+), otherwise an empty list.</returns>
    public static IReadOnlyList<PlanAssignment> AmbiguousCandidates(
        Guid? selectedPlanAssignmentId,
        DateOnly txDate,
        string txCurrency,
        IEnumerable<PlanAssignment> payeeAssignments,
        IReadOnlyDictionary<Guid, string> planCurrencyById,
        IReadOnlySet<Guid> archivedPlanIds)
    {
        // An explicit choice removes the ambiguity by definition — the admin already stated the plan.
        if (selectedPlanAssignmentId.HasValue)
            return [];

        // Archived plans are excluded by Candidates, so a retired plan no longer counts as one of the
        // "two eligible plans" that make a transaction ambiguous. It never was a real alternative.
        var candidates = PlanAssignmentResolver.Candidates(
            payeeAssignments, txDate, txCurrency, planCurrencyById, archivedPlanIds);

        // 0 candidates → already surfaced as NoActiveAssignment / CurrencyMismatch.
        // 1 candidate  → unambiguous; resolves exactly as it always has.
        return candidates.Count >= 2 ? candidates : [];
    }

    public static IReadOnlyList<PlanAssignment> AmbiguousCandidates(
        CompensationTransaction transaction,
        IEnumerable<PlanAssignment> payeeAssignments,
        IReadOnlyDictionary<Guid, string> planCurrencyById,
        IReadOnlySet<Guid> archivedPlanIds) =>
        AmbiguousCandidates(
            transaction.SelectedPlanAssignmentId, transaction.TransactionDate,
            transaction.Amount.Currency, payeeAssignments, planCurrencyById, archivedPlanIds);

    public static bool IsAmbiguous(
        CompensationTransaction transaction,
        IEnumerable<PlanAssignment> payeeAssignments,
        IReadOnlyDictionary<Guid, string> planCurrencyById,
        IReadOnlySet<Guid> archivedPlanIds) =>
        AmbiguousCandidates(transaction, payeeAssignments, planCurrencyById, archivedPlanIds).Count > 0;

    /// <summary>
    /// The same rule as an <c>IQueryable</c>, so the reconciliation queue can page and aggregate over
    /// it in SQL instead of materialising every Pending row in the tenant.
    ///
    /// ★★ THIS IS A SECOND EXPRESSION OF ONE RULE, AND THAT IS A RISK THE CODEBASE HAS BEEN BITTEN BY.
    /// It exists because <see cref="PlanAssignmentResolver.Candidates"/> is in-memory by design — the
    /// engine calls it per transaction inside a batch it has already loaded — while a screen that
    /// says "you owe exactly this" has to compute its totals from the same query that produced its
    /// rows, which means SQL. The two are kept honest by
    /// <c>AmbiguousAttributionQueryableAgreesWithCandidatesTests</c>, which runs both over the same
    /// data and requires identical answers. If that test ever goes red, the queryable is wrong — the
    /// in-memory version is the engine's, and the engine is the authority.
    ///
    /// ★ THE CLAUSES ARE IN THE SAME ORDER AS Candidates, one for one: not archived, Active, period
    /// covers the date, plan currency equals the transaction currency. Plus the two conditions the
    /// scalar overload checks before it delegates — no declared choice, and a payee at all.
    /// </summary>
    public static IQueryable<CompensationTransaction> Queryable(IApplicationDbContext db) =>
        db.CompensationTransactions.Where(t =>
            t.Status == CompensationTransactionStatus.Pending
            && t.PayeeId != null
            && t.SelectedPlanAssignmentId == null
            && db.PlanAssignments.Count(a =>
                a.PayeeId == t.PayeeId
                && a.Status == AssignmentStatus.Active
                && a.EffectivePeriod.Start <= t.TransactionDate
                && a.EffectivePeriod.End >= t.TransactionDate
                && db.CompensationPlans.Any(p =>
                    p.Id == a.PlanId
                    && p.Status != PlanStatus.Archived
                    && p.Currency == t.Amount.Currency)) >= 2);
}
