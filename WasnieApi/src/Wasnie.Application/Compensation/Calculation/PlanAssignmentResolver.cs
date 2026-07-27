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
    /// The assignments that could legitimately carry this transaction: Active, covering the date,
    /// and denominated in the transaction's currency. This is THE eligibility rule — the plan
    /// selector offered to the admin must be built from this same method, so the options shown are
    /// exactly the ones the engine would consider. Re-deriving it anywhere else is how a UI starts
    /// promising an attribution the engine will not honour.
    /// </summary>
    public static IReadOnlyList<PlanAssignment> Candidates(
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
            return []; // No plan covers this date → stay Pending.

        // Step 2: filter by currency match (Pattern B core rule).
        return dateCandidates
            .Where(pa =>
                planCurrencyById.TryGetValue(pa.PlanId, out var planCcy) &&
                string.Equals(planCcy, txCurrency, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

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
        var currencyMatched = Candidates(allPayeeAssignments, txDate, txCurrency, planCurrencyById);

        if (currencyMatched.Count == 0)
            return null; // No plan in this currency → stay Pending (another plan may be created later).

        if (currencyMatched.Count == 1)
            return currencyMatched[0];

        // Tie-break for multiple matching assignments (same currency, overlapping dates):
        // 1. Shortest effective period (most specific coverage wins).
        // 2. Smallest Id for deterministic stable ordering.
        //
        // This arbitrary tie-break is exactly what an explicit admin selection exists to avoid — see
        // ResolveSelected. It remains the behaviour for every transaction that carries no selection
        // (Excel, HubSpot, rows created before the field existed).
        return currencyMatched
            .OrderBy(pa => pa.EffectivePeriod!.End.DayNumber - pa.EffectivePeriod.Start.DayNumber)
            .ThenBy(pa => pa.Id)
            .First();
    }

    /// <summary>
    /// Honours an EXPLICIT admin attribution: the transaction must be credited to
    /// <paramref name="selectedAssignmentId"/> and to nothing else.
    ///
    /// The selection is only honoured while it is still one of <see cref="Candidates"/>. If it stopped
    /// being eligible between ingest and processing (assignment deactivated, period changed, plan
    /// re-denominated), this returns a rejection with a readable reason so the caller can fail loudly.
    /// It never falls back to the tie-break: silently crediting a DIFFERENT plan than the one the admin
    /// chose is the precise failure this whole mechanism exists to prevent.
    /// </summary>
    public static SelectedAssignmentResolution ResolveSelected(
        IEnumerable<PlanAssignment> allPayeeAssignments,
        DateOnly txDate,
        string txCurrency,
        IReadOnlyDictionary<Guid, string> planCurrencyById,
        Guid selectedAssignmentId)
    {
        var all = allPayeeAssignments as IReadOnlyList<PlanAssignment> ?? allPayeeAssignments.ToList();

        var selected = all.FirstOrDefault(pa => pa.Id == selectedAssignmentId);
        if (selected is null)
            return SelectedAssignmentResolution.Rejected(
                $"The plan assignment chosen for this transaction ({selectedAssignmentId}) no longer belongs to its payee. " +
                "Re-select the plan on the transaction.");

        if (selected.Status != AssignmentStatus.Active)
            return SelectedAssignmentResolution.Rejected(
                "The plan assignment chosen for this transaction is no longer active. " +
                "Re-activate it or re-select the plan on the transaction.");

        if (selected.EffectivePeriod is null ||
            selected.EffectivePeriod.Start > txDate ||
            selected.EffectivePeriod.End < txDate)
            return SelectedAssignmentResolution.Rejected(
                "The plan assignment chosen for this transaction no longer covers the transaction date. " +
                "Adjust the assignment period or re-select the plan on the transaction.");

        if (!planCurrencyById.TryGetValue(selected.PlanId, out var planCurrency) ||
            !string.Equals(planCurrency, txCurrency, StringComparison.OrdinalIgnoreCase))
            return SelectedAssignmentResolution.Rejected(
                $"The plan chosen for this transaction is not denominated in '{txCurrency}'. " +
                "Re-select the plan on the transaction.");

        return SelectedAssignmentResolution.Accepted(selected);
    }
}

/// <summary>
/// Outcome of honouring an explicit admin plan selection. Either the assignment to credit, or a
/// human-readable reason why the selection can no longer be honoured — never a silent substitution.
/// </summary>
public sealed record SelectedAssignmentResolution(PlanAssignment? Assignment, string? RejectionReason)
{
    public bool IsAccepted => Assignment is not null;

    public static SelectedAssignmentResolution Accepted(PlanAssignment assignment) => new(assignment, null);
    public static SelectedAssignmentResolution Rejected(string reason) => new(null, reason);
}
