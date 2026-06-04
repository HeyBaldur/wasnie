using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Calculation;

/// <summary>
/// Pattern B plan selection: when a payee has multiple active PlanAssignments
/// covering a transaction date, the assignment whose Plan currency matches the
/// transaction currency is the one that applies.
///
/// Returns null when no assignment covers the date, or when no assignment in the
/// matching currency exists — in both cases the transaction stays Pending.
/// Currency mismatch is a routing signal, NOT an error.
/// </summary>
public static class PlanAssignmentResolver
{
    /// <summary>
    /// Resolves the single PlanAssignment that applies to a transaction.
    /// All caller-supplied data must already be loaded (no DB access).
    /// </summary>
    /// <param name="allPayeeAssignments">All PlanAssignments for the payee (any status/period).</param>
    /// <param name="txDate">The transaction's date.</param>
    /// <param name="txCurrency">The transaction's currency (ISO 4217).</param>
    /// <param name="planCurrencyById">Dictionary of PlanId → Plan.Currency for quick lookup.</param>
    /// <returns>The matching PlanAssignment, or null if none apply.</returns>
    public static PlanAssignment? Resolve(
        IEnumerable<PlanAssignment> allPayeeAssignments,
        DateOnly txDate,
        string txCurrency,
        IReadOnlyDictionary<Guid, string> planCurrencyById)
    {
        // Step 1: active assignments whose effective period covers the transaction date.
        var dateCandidates = allPayeeAssignments
            .Where(pa =>
                pa.Status == AssignmentStatus.Active &&
                pa.EffectivePeriod is not null &&
                pa.EffectivePeriod.Start <= txDate &&
                pa.EffectivePeriod.End >= txDate)
            .ToList();

        if (dateCandidates.Count == 0)
            return null; // No plan covers this date → stay Pending.

        // Step 2: filter by currency match (Pattern B core rule).
        var currencyMatched = dateCandidates
            .Where(pa =>
                planCurrencyById.TryGetValue(pa.PlanId, out var planCcy) &&
                string.Equals(planCcy, txCurrency, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (currencyMatched.Count == 0)
            return null; // No plan in this currency → stay Pending (another plan may be created later).

        if (currencyMatched.Count == 1)
            return currencyMatched[0];

        // Tie-break for multiple matching assignments (same currency, overlapping dates):
        // 1. Shortest effective period (most specific coverage wins).
        // 2. Smallest Id for deterministic stable ordering.
        return currencyMatched
            .OrderBy(pa => pa.EffectivePeriod!.End.DayNumber - pa.EffectivePeriod.Start.DayNumber)
            .ThenBy(pa => pa.Id)
            .First();
    }
}
