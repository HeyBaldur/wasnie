using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Assistant.Tools;

/// <summary>
/// WHAT A RATE VALUE MEANS, as a token instead of a sentence.
///
/// ★ WHY A TOKEN AND NOT PROSE. The number `0.05` on a rate table is five per cent under one
/// combination and five euros under another, and the difference is not visible in the number. The
/// assistant cannot be allowed to work that out — it already got it wrong, inventing a rate mode that
/// does not exist and explaining Units as if it were a percentage. So the backend states the semantics
/// and the model is taught, once, in the system prompt, what each token means.
///
/// ★ AND NOT A SENTENCE, because a sentence written here would be English written by the domain: it
/// would have to be translated for the ES and PL users the product already has, and the translation
/// would live in the wrong layer. A token is language-neutral; the model renders it in the user's
/// language.
///
/// ★ THE ENUM IS CLOSED, AND THAT IS ENFORCED. <see cref="PlanRuleSemantics.Describe"/> throws for any
/// combination it has no token for, rather than folding it into the nearest one — a rate silently
/// described with the wrong semantics is exactly the failure this type exists to prevent, and a loud
/// failure becomes a retry card instead of a confident wrong explanation.
/// </summary>
public enum RateSemantic
{
    /// <summary>
    /// The raw value is a FRACTION of the base amount: 0.05 → 5%, 1.00 → 100%.
    /// Engine: <c>CommissionCalculator.ComputeCommission</c>, <c>RateTableType.Flat</c> branch —
    /// <c>baseAmount.Multiply(FlatRate)</c>.
    /// </summary>
    FractionalMultiplierOfBase,

    /// <summary>
    /// The raw value is an AMOUNT OF MONEY earned per unit sold: 2.00 → €2.00 per unit.
    /// Engine: <c>CommissionCalculator.ComputeUnitsCommission</c> — <c>ratePerUnit × quantity</c>.
    /// </summary>
    CurrencyAmountPerUnit,

    /// <summary>
    /// Brackets over ABSOLUTE amounts of the base, each bracket's raw rate being a fraction applied to
    /// the portion of the base that falls inside it (progressive, not a single rate).
    /// Engine: <c>CommissionCalculator.ComputeTieredCommission</c>.
    /// </summary>
    FractionalRatePerRevenueBracket,

    /// <summary>
    /// Brackets over QUOTA ATTAINMENT expressed as a fraction (1.00 = 100% of quota). The bracket
    /// containing the payee's attainment supplies ONE fractional rate, applied to the whole base.
    /// Engine: <c>CommissionCalculator.ComputeAttainmentCommission</c>.
    /// </summary>
    FractionalMultiplierFromAttainmentBracket,

    /// <summary>
    /// The same attainment brackets, but the transaction is SPLIT at the quota boundaries: each
    /// bracket's fractional rate is earned on the portion of the transaction that falls within that
    /// bracket's absolute revenue range (<c>attainmentFraction × quotaTarget</c>).
    /// Engine: <c>CommissionCalculator.ComputeAttainmentSplitCommission</c>.
    /// </summary>
    FractionalRateSplitAtQuotaBoundary,

    /// <summary>
    /// A COMBINATION THE ENGINE REFUSES TO CALCULATE: unit-based measurement with a non-flat rate
    /// table. The domain rejects it at save time (<c>Rule.ValidateMeasurementRateTableCompatibility</c>),
    /// and the engine's runtime safety net logs an error and credits ZERO
    /// (<c>CreditAllocationService.BuildCreditsAsync</c>). It is a token rather than a throw because it
    /// is a state a stored rule can genuinely be in, and a plan that pays nothing is the single most
    /// useful thing the assistant can tell someone asking why they were not paid.
    /// </summary>
    NoCommissionUnsupportedCombination,
}

/// <summary>
/// WHAT THE RATE IS APPLIED TO. Separate from <see cref="RateSemantic"/> because the engine chooses the
/// base from the MEASUREMENT and the rate meaning from the RATE TABLE, and collapsing the two would hide
/// the one that surprises people.
///
/// ★ THE SURPRISE THIS EXISTS TO SURFACE. <c>MeasurementType</c> has five members, and the engine
/// branches on exactly one of them: <c>Units</c> takes the quantity path, and EVERYTHING ELSE —
/// Revenue, Margin, Attainment, Custom — uses the transaction's amount. A rule configured as "Margin"
/// is calculated on gross transaction amount, not on margin. Reporting the measurement name alone would
/// let the model describe a margin plan that does not exist.
/// </summary>
public enum MeasurementBase
{
    /// <summary>The transaction's money amount. Engine: <c>baseAmount = transaction.Amount</c>.</summary>
    TransactionAmount,

    /// <summary>The transaction's unit count. Engine: <c>ComputeUnitsCommission(transaction.Quantity, …)</c>.</summary>
    TransactionQuantity,
}

/// <summary>
/// The mapping from a stored rule's configuration to the tokens above.
///
/// ★ THIS IS A MIRROR, AND THE MIRROR IS TESTED AGAINST THE ORIGINAL. The engine
/// (<c>CommissionCalculator</c> + <c>CreditAllocationService</c>) lives in Wasnie.Infrastructure and is
/// internal, so this cannot call it — a copy is the only option, and a copy that drifts would have the
/// assistant explaining a calculation the engine stopped performing. The unit tests therefore do not
/// assert the tokens against a second copy of the expectation: they run the REAL calculator and assert
/// that its arithmetic matches what each token claims. Change the engine and the token test fails.
/// </summary>
public static class PlanRuleSemantics
{
    /// <summary>
    /// What the rate applied to, derived the same way the engine derives it.
    ///
    /// Every member of <see cref="MeasurementType"/> is listed by name on purpose. A future member
    /// would fall through to the throw instead of silently inheriting "transaction amount", because
    /// whether it does is a question about money that somebody has to answer deliberately.
    /// </summary>
    public static MeasurementBase BaseOf(MeasurementType measurement) => measurement switch
    {
        MeasurementType.Units => MeasurementBase.TransactionQuantity,

        // The engine's `else` branch, spelled out. All four use transaction.Amount today.
        MeasurementType.Revenue => MeasurementBase.TransactionAmount,
        MeasurementType.Margin => MeasurementBase.TransactionAmount,
        MeasurementType.Attainment => MeasurementBase.TransactionAmount,
        MeasurementType.Custom => MeasurementBase.TransactionAmount,

        _ => throw new NotSupportedException(
            $"No assistant token describes the measurement type '{measurement}'. Add one to " +
            $"{nameof(MeasurementBase)} rather than letting the assistant guess what the rate applies to."),
    };

    /// <summary>
    /// What the rate VALUE means for this (rate table, measurement) pair.
    ///
    /// <paramref name="splitAtQuota"/> only matters for attainment tables, and it changes the semantics
    /// completely — bracket lookup pays one rate on everything, split-at-quota pays each bracket's rate
    /// on its own slice. Two different answers to "how much do I earn on this deal".
    /// </summary>
    public static RateSemantic Describe(
        RateTableType rateTable, MeasurementType measurement, bool splitAtQuota)
    {
        var appliedTo = BaseOf(measurement);

        if (appliedTo == MeasurementBase.TransactionQuantity)
        {
            // Units + anything other than Flat: the domain rejects it on save and the engine credits
            // zero. Not an error to raise — a stored rule really can be in this state.
            return rateTable == RateTableType.Flat
                ? RateSemantic.CurrencyAmountPerUnit
                : RateSemantic.NoCommissionUnsupportedCombination;
        }

        return rateTable switch
        {
            RateTableType.Flat => RateSemantic.FractionalMultiplierOfBase,
            RateTableType.Tiered => RateSemantic.FractionalRatePerRevenueBracket,
            RateTableType.AttainmentBased => splitAtQuota
                ? RateSemantic.FractionalRateSplitAtQuotaBoundary
                : RateSemantic.FractionalMultiplierFromAttainmentBracket,

            _ => throw new NotSupportedException(
                $"No assistant token describes rate table '{rateTable}' with measurement '{measurement}'. " +
                $"Add one to {nameof(RateSemantic)} — forcing it into an existing token would have the " +
                "assistant explain a calculation the engine does not perform."),
        };
    }
}
