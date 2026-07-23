using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Assignments;
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
        IReadOnlyDictionary<Guid, string> planCurrencyById)
    {
        // An explicit choice removes the ambiguity by definition — the admin already stated the plan.
        if (selectedPlanAssignmentId.HasValue)
            return [];

        var candidates = PlanAssignmentResolver.Candidates(
            payeeAssignments, txDate, txCurrency, planCurrencyById);

        // 0 candidates → already surfaced as NoActiveAssignment / CurrencyMismatch.
        // 1 candidate  → unambiguous; resolves exactly as it always has.
        return candidates.Count >= 2 ? candidates : [];
    }

    public static IReadOnlyList<PlanAssignment> AmbiguousCandidates(
        CompensationTransaction transaction,
        IEnumerable<PlanAssignment> payeeAssignments,
        IReadOnlyDictionary<Guid, string> planCurrencyById) =>
        AmbiguousCandidates(
            transaction.SelectedPlanAssignmentId, transaction.TransactionDate,
            transaction.Amount.Currency, payeeAssignments, planCurrencyById);

    public static bool IsAmbiguous(
        CompensationTransaction transaction,
        IEnumerable<PlanAssignment> payeeAssignments,
        IReadOnlyDictionary<Guid, string> planCurrencyById) =>
        AmbiguousCandidates(transaction, payeeAssignments, planCurrencyById).Count > 0;
}
