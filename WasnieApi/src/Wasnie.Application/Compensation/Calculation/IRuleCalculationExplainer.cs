using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Transactions;

namespace Wasnie.Application.Compensation.Calculation;

/// <summary>
/// Runs one rule over one transaction and reports how it reached its number.
///
/// ★★ WHY THIS INTERFACE EXISTS AT ALL. The engine, <c>CommissionCalculator</c>, is
/// <c>internal static</c> inside Wasnie.Infrastructure, so nothing in Application can call it — and
/// Application is where a query handler would live. The alternatives were to make the engine public
/// (widening the surface of the calculation core to the whole solution, for one caller) or to move
/// it (a much larger change to a file that computes people's pay). An interface here with the
/// implementation next to the engine is the smallest of the three: the engine's visibility does not
/// change, and Application depends on a contract rather than on Infrastructure.
///
/// ★ IT ANSWERS, IT DOES NOT DECIDE. This reports what the engine did, including when what the
/// engine did was fall back on a default. Refusing to show a number — for a rule whose attainment
/// nobody supplied, say — is a policy call that belongs to whoever is asking, not here; the trace
/// gives them what they need to make it (see <see cref="AttainmentSource"/>).
///
/// ★ NOTHING IS PERSISTED. The engine's core is pure and this adds no writes: no credit, no ledger
/// entry, no counter. Explaining a payout must never be able to change one.
/// </summary>
public interface IRuleCalculationExplainer
{
    /// <param name="attainmentPct">
    /// The attainment ratio to evaluate a bracket-lookup attainment table against. ★ Null means
    /// "nobody supplied one", and the trace will say so rather than passing off the engine's 1.0
    /// default as a measurement.
    /// </param>
    /// <param name="splitContext">
    /// Prior cumulative and quota target for a split-at-quota table. Null yields the engine's own
    /// no-quota outcome — zero commission — reported as a skipped rate step, not as a rule that
    /// simply earns nothing.
    /// </param>
    RuleCalculationTrace Explain(
        Rule rule,
        CompensationTransaction transaction,
        string planCurrency,
        decimal? attainmentPct = null,
        AttainmentSplitContext? splitContext = null);
}
