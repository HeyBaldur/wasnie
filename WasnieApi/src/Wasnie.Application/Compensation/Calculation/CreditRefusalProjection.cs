namespace Wasnie.Application.Compensation.Calculation;

/// <summary>
/// Projects a <see cref="RuleCalculationTrace"/> onto the one queryable fact a reconciliation screen
/// needs from it: WHY the rate component refused to pay, if it did.
///
/// ★★ THE COLUMN IS AN INDEX, NOT A SECOND SOURCE OF TRUTH. <c>Credits.CalculationTrace</c> remains
/// the evidence — the whole cascade, every threshold, every tier walked. This projection exists
/// because that document is deliberately opaque to SQL (<c>CreditConfiguration.cs</c>: "stored as
/// text and never queried by SQL"), and a reconciliation queue has to FILTER and GROUP BY over the
/// refusal across every credit in a tenant. Scanning nvarchar(max) for that would work today, at ten
/// traces, and would be a full table scan per page and per aggregate for the rest of the product's
/// life — and it would make the document's internal field names a query contract, which is exactly
/// the coupling the trace was designed to avoid.
///
/// ★★ THE ANTI-DRIFT RULE, AND IT IS THE WHOLE REASON THIS IS ONE FUNCTION. The column and the
/// document are written in the SAME act, from the SAME trace object, by the caller that already has
/// it (<c>CreditAllocationService</c>). There is no second derivation, no later job, no reader that
/// recomputes. A denormalisation that can be produced by two paths is a denormalisation that will
/// eventually disagree with itself, and disagreeing about why somebody was not paid is not a
/// tolerable failure. <c>CreditRefusalProjectionTests</c> pins it by comparing the column against the
/// value parsed back out of the serialised document.
///
/// ★ IT RETURNS A STRING, NOT THE ENUM, AND THAT IS LAYERING RATHER THAN LAZINESS.
/// <see cref="RateRefusalReason"/> lives in this Application layer; <c>Credit</c> lives in Domain and
/// cannot reference it. The domain stays blind to the trace's vocabulary — it stores a code it never
/// interprets, exactly as it already stores the document it never parses. The text also matches how
/// the trace itself encodes the enum, so the column and the document read identically.
/// </summary>
public static class CreditRefusalProjection
{
    /// <summary>
    /// The refusal code for a trace, or null when the rate component did not refuse.
    ///
    /// ★ NULL HAS THREE HONEST MEANINGS AND THEY ARE ALL "NOTHING TO SHOW IN THE QUEUE": no engine
    /// run recorded this credit, the trigger never matched so there was no rate step, or the rate
    /// component priced the sale. None of them is a refusal, and none of them may be presented as one.
    /// </summary>
    public static string? FromTrace(RuleCalculationTrace? trace)
    {
        var rate = trace?.Steps.FirstOrDefault(s => s.Component == RuleCalculationComponent.Rate);

        // ★ READ FROM THE FIELD, NEVER INFERRED FROM Skipped. A Skipped rate step is not always a
        // refusal to pay for want of a priceable table — the Units misconfiguration guard also emits
        // one — and, more importantly, the five traces KAN-26 tanda 2 wrote express their refusal as
        // Skipped + NoTarget because the code did not exist yet. Reconstructing those here would give
        // this projection two grammars, the current one and an archaeological one, with the second
        // deciding on its own what old money meant. They stay null and stay visibly null.
        return rate?.RateRefusal?.ToString();
    }
}
