using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Rules;

/// <summary>
/// The one thing the six ladder invariants never looked at: THE VALUE OF THE RATE ITSELF.
///
/// ★★ WHAT THIS EXISTS TO STOP. A rate is stored as a MULTIPLIER — 0.04 is 4%. Somebody typing the
/// per cent they mean, `4`, saves 400%, and the engine multiplies the sale by it without a word
/// (<c>CommissionCalculator.ComputeAttainmentCommission</c>: <c>baseAmount.Multiply(tier.Rate)</c>).
/// Reproduced on 2026-09-01: a base of 50,000 produced a credit of 200,000. Every one of
/// <see cref="RateTableInvariant"/>'s six rules passed on that table, because all six read
/// <c>From</c> and <c>To</c> and none reads <c>Rate</c>.
///
/// ★★ THE CEILING IS 1, AND IT IS NOT ARBITRARY — IT IS WHAT THE DATA SAYS. Across the 60 rules in
/// PlanRules, the largest tiered rate is 0.15 and the largest attainment rate is 0.09; the only
/// tier rate above 1 anywhere is the 4/7 of the rule that reproduced the bug. So no legitimate
/// ladder is refused by this. The comparison is STRICTLY GREATER for a reason of its own: one real
/// flat rule ("Full payout") is exactly 1.00, a referral that pays the entire sale, and it must keep
/// saving. 100% is unusual; 400% is a typo.
///
/// ★★ FLAT IS NOT ALWAYS A FRACTION, WHICH IS WHY THIS CLASS HAS TWO ENTRY POINTS INSTEAD OF ONE.
/// With <c>MeasurementType.Units</c> the flat rate is MONEY PER UNIT — €3 or €5 a unit, and four such
/// rules exist in production. <c>Rule.cs</c>'s compatibility check confines Units to Flat tables, so
/// tiered and attainment rates are unconditionally fractions and can be checked without context;
/// a flat rate cannot, and <see cref="ValidateFlatRateForWrite"/> takes the measurement in order to
/// stand aside for Units. A single blanket ceiling on <c>RateTable.Flat</c> would have refused the
/// €5-per-unit spiffs — a validation that blocks a correct configuration sends its user to support.
///
/// ★ WHERE IT MAY NOT GO: <c>Rule.Create</c>. That is the constructor <c>Plan.CloneAsNewVersion</c>
/// uses, and cloning into a fresh Draft is the ONLY route to correcting a rule on an active plan.
/// A stored rule with a rate of 4 already exists; validating in <c>Rule.Create</c> would lock it in
/// place forever behind the very rule meant to help (§D4). The ladder check therefore lives in the
/// factories, which the clone does not call, and the flat check in <c>Plan.AddRule</c>/
/// <c>Plan.UpdateRule</c>, which the clone does not call either.
/// </summary>
public static class RateMagnitude
{
    /// <summary>
    /// The most a rate expressed as a fraction of its base may be: 1 is 100% of the sale.
    ///
    /// ★ A NAMED CONSTANT AND NOT A LITERAL, because it is echoed back to the reader inside the
    /// refusal ("at most {{maximum}}"). A bare 1 buried in two comparisons and three translation
    /// files is four places to change and three chances to disagree.
    /// </summary>
    public const decimal MaxFractionalRate = 1m;

    /// <summary>
    /// Checks one rate that is known to be a fraction of a money base — every tiered and attainment
    /// tier, and a flat rate outside Units mode.
    /// </summary>
    /// <param name="tierNumber">
    /// The 1-based position of the tier this rate belongs to, or null for a flat table, which has no
    /// tiers.
    ///
    /// ★ NULL MEANS THE KEY IS NOT SENT AT ALL, not that it is sent empty. The client branches on
    /// its ABSENCE to choose between "the rate of tier 2" and "the rate", and it also interpolates
    /// every parameter it receives into the sentence — so a `tierNumber` of null would print the
    /// word "null" on the screen of the one case that has no tier to name.
    /// </param>
    public static void ValidateFractionalRate(decimal rate, int? tierNumber)
    {
        var tier = tierNumber is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?> { ["tierNumber"] = tierNumber };

        // Below zero is a separate mistake from above the ceiling and gets a separate sentence: one
        // is a rule that would take money back on every sale, the other is a decimal point.
        if (rate < 0m)
        {
            throw new DomainCodedException(
                RateTableInvariant.RateBelowZero,
                new Dictionary<string, object?>(tier) { ["rate"] = rate });
        }

        if (rate > MaxFractionalRate)
        {
            throw new DomainCodedException(
                RateTableInvariant.RateAboveMaximum,
                new Dictionary<string, object?>(tier) { ["rate"] = rate, ["maximum"] = MaxFractionalRate });
        }
    }

    /// <summary>
    /// Checks a flat rate on the way into a plan, WITH the measurement that says what it means.
    ///
    /// Called from <c>Plan.AddRule</c> and <c>Plan.UpdateRule</c> — the write doors — and from
    /// nowhere on the clone path or the read path. A non-Flat table is not this method's business:
    /// its tiers were already checked by the factory that built it.
    /// </summary>
    public static void ValidateFlatRateForWrite(Measurement measurement, RateTable rateTable)
    {
        if (rateTable.Type != RateTableType.Flat)
        {
            return;
        }

        // ★ THE UNITS CARVE-OUT. Here FlatRate is currency per unit (€2.00 a unit), not a share of
        // anything, so there is no ceiling to apply — see ComputeUnitsCommission. Rule.Create
        // guarantees the pairing runs only one way: Units implies Flat, so no tiered or attainment
        // rate can ever reach this exemption.
        if (measurement.Type == MeasurementType.Units)
        {
            return;
        }

        if (rateTable.FlatRate is not { } rate)
        {
            // A Flat table with no rate is refused by RateTableRequest.ToDomain long before here.
            // Saying nothing is right: inventing a second refusal for the same hole would just race
            // the first one.
            return;
        }

        ValidateFractionalRate(rate, tierNumber: null);
    }
}
