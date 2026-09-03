using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Application.Compensation.Calculation;

/// <summary>
/// Which part of a rule produced a step. The order of the enum is documentation, not behaviour: the
/// steps come back in the order the engine actually ran them, which is what
/// <see cref="RuleCalculationTrace.Steps"/> preserves.
/// </summary>
public enum RuleCalculationComponent
{
    Trigger,
    Base,
    Rate,
    Modifier,
    Cap,
    Floor,
}

/// <summary>
/// What happened to a component.
///
/// ★ THE DISTINCTION THAT MATTERS IS BETWEEN THE MIDDLE THREE. Arithmetically, a rule with no cap
/// and a rule whose cap the commission never reached produce exactly the same amount. To somebody
/// asking why they were paid what they were paid, "this rule has no ceiling" and "there is a ceiling
/// and you did not hit it" are different answers, and only one of them is reassuring.
/// </summary>
public enum RuleCalculationOutcome
{
    /// <summary>The rule does not configure this component at all.</summary>
    NotConfigured,

    /// <summary>Configured, evaluated, and it moved the running amount.</summary>
    Applied,

    /// <summary>Configured and evaluated, but the running amount came out unchanged.</summary>
    AppliedWithoutEffect,

    /// <summary>
    /// Configured, but the engine declined to apply it — a cap or floor denominated in another
    /// currency, or a cap scope this engine does not honour. ★ Surfaced rather than folded into
    /// "no effect" precisely because it is a misconfiguration wearing the costume of a working rule.
    /// </summary>
    Skipped,

    /// <summary>
    /// Trigger only: the conditions did not match, so NO credit is created at all. ★ This is not a
    /// credit of zero, and the two must never be shown as the same thing — one means the rule did
    /// not apply to this transaction, the other means it applied and paid nothing.
    /// </summary>
    NotMatched,
}

/// <summary>One tier of a tiered table and the slice of the transaction that fell inside it.</summary>
public sealed record RateTierStep(decimal From, decimal? To, decimal Rate, decimal Portion, Money Amount);

/// <summary>
/// One component of the cascade, as the engine ran it.
///
/// ★ THE FIELDS ARE FACTS, NOT SENTENCES. No display text is authored here: a step carries the
/// numbers and the outcome, and whatever renders it owns the wording and the language. An engine
/// that emits prose is an engine that has to be redeployed to fix a translation.
/// </summary>
public sealed record RuleCalculationStep
{
    public required RuleCalculationComponent Component { get; init; }
    public required RuleCalculationOutcome Outcome { get; init; }

    /// <summary>The running amount entering this component. Null on Trigger, which has no money.</summary>
    public Money? Input { get; init; }

    /// <summary>The running amount leaving this component. Null on Trigger.</summary>
    public Money? Output { get; init; }

    /// <summary>
    /// The component's own scalar: the flat rate, the modifier factor, the unit quantity, the
    /// attainment percentage. Null where the component has no single scalar (tiered tables carry
    /// <see cref="Tiers"/> instead).
    /// </summary>
    public decimal? Operand { get; init; }

    /// <summary>The cap or floor amount that was compared against.</summary>
    public Money? Threshold { get; init; }

    /// <summary>Which kind of rate table produced a <see cref="RuleCalculationComponent.Rate"/> step.</summary>
    public RateTableType? RateTable { get; init; }

    /// <summary>The tiers actually walked, for tiered and split-at-quota tables.</summary>
    public IReadOnlyList<RateTierStep>? Tiers { get; init; }

    /// <summary>
    /// ★★ WHERE THE ATTAINMENT PERCENTAGE CAME FROM, on attainment rate steps. Null everywhere else.
    ///
    /// Three states, not two, and collapsing them is the failure this field exists to prevent. A
    /// breakdown that says "attainment 100% → 8% bracket" is true when the 100% was measured against
    /// a real quota, an assumption when a simulator supplied it, and a lie when nobody provided
    /// anything and the engine's own default of 1.0 answered — a rep at full quota, which is a
    /// figure that looks entirely reasonable and is false for almost everybody.
    /// </summary>
    public AttainmentSource? AttainmentSource { get; init; }

    /// <summary>
    /// ★★ WHY A <see cref="RuleCalculationOutcome.Skipped"/> RATE STEP PAID NOTHING. Null on every
    /// step the engine actually priced.
    ///
    /// Skipped already says the engine declined; this says what it declined over, so that ONE query
    /// finds every refusal the rate component can make instead of one query per shape of hole. It is
    /// a CODE and not a sentence for the usual reason (§C1): an engine that emits prose has to be
    /// redeployed to fix a translation.
    ///
    /// ★ APPENDED, NEVER REORDERED, for the same reason as <see cref="Calculation.AttainmentSource"/>:
    /// the trace is persisted, and while it is persisted as text a client reading by ordinal would
    /// have every stored refusal silently reinterpreted.
    /// </summary>
    public RateRefusalReason? RateRefusal { get; init; }
}

/// <summary>
/// Why the rate component refused to price a transaction. See <see cref="RuleCalculationStep.RateRefusal"/>.
///
/// ★★ EVERY MEMBER IS A CASE THE ENGINE USED TO PAY SILENTLY. Each of these once produced a number
/// — a zero, or a partial amount over the slice of the sale the table happened to cover — stamped
/// <see cref="RuleCalculationOutcome.Applied"/> and indistinguishable from a real calculation. The
/// engine now pays nothing in these cases and says which one it was, because a rule that cannot
/// justify an amount must not invent one.
/// </summary>
public enum RateRefusalReason
{
    /// <summary>
    /// No quota in effect for this payee, plan and date — or one whose target is zero. The rule pays
    /// by attainment and there is nothing to measure against. KAN-26 tanda 2; the accompanying
    /// <see cref="RuleCalculationStep.AttainmentSource"/> is
    /// <see cref="Calculation.AttainmentSource.NoTarget"/>.
    /// </summary>
    NoQuotaInEffect,

    /// <summary>
    /// The attainment ladder has no bracket containing this ratio, so the table states no rate for
    /// this rep. ★ NOT the same as a rate of zero: zero is a decision somebody made, and this is a
    /// ladder that never mentions the case.
    /// </summary>
    NoMatchingBracket,

    /// <summary>
    /// The ladder priced only part of the transaction and stops below the rest of it — a bounded top
    /// tier, or a gap the amount falls into. ★ THIS IS THE ONE THAT LOOKED MOST LIKE A REAL PAYMENT:
    /// the engine paid the covered slice and dropped the remainder, so a 1,000,000 EUR sale under a
    /// ladder that stops at 10,000 produced a perfectly ordinary-looking 820 EUR.
    /// </summary>
    AmountOutsideTable,
}

/// <summary>
/// Where an attainment percentage came from. See <see cref="RuleCalculationStep.AttainmentSource"/>.
/// </summary>
public enum AttainmentSource
{
    /// <summary>Read from the payee's real quota — the pay run's case.</summary>
    Measured,

    /// <summary>Handed in by the caller as an assumption, e.g. a simulator's "assume 100%".</summary>
    Supplied,

    /// <summary>
    /// ★ Nobody provided one and the engine's 1.0 default answered. A number produced this way must
    /// never be presented as a fact about anyone.
    /// </summary>
    Defaulted,

    /// <summary>
    /// ★★ THERE WAS NOTHING TO MEASURE AGAINST — no quota in effect for this payee and plan on this
    /// date, or one whose target is zero. The percentage that comes with this is 0, and it is 0
    /// because nobody set a target, NOT because the rep sold nothing.
    ///
    /// Those two are indistinguishable in the stored number and opposite in meaning: one is a
    /// configuration hole and the other is a real, and terrible, quarter. Until this member existed
    /// both arrived sealed as <see cref="Measured"/>, so a breakdown could state as measured fact
    /// that somebody achieved 0% of a quota that was never set.
    ///
    /// ★ IT CANNOT BE DERIVED FROM THE PERCENTAGE. A genuine 0% against a real target is also 0, so
    /// the source has to be carried from where the target was looked up. See
    /// <c>QuotaAttainmentService.ComputeAsync</c>.
    ///
    /// ★ APPENDED, NOT INSERTED. Some surfaces serialise this enum by name and some clients could
    /// read it by ordinal; reordering would silently reinterpret every value that ever crossed the
    /// wire. New members go at the end, always.
    /// </summary>
    NoTarget,
}

/// <summary>
/// The result of running one rule over one transaction, with the reasoning attached.
///
/// ★ <see cref="CreditGenerated"/> IS NOT DERIVABLE FROM <see cref="Commission"/>. A rule whose
/// trigger did not match produces no credit; a rule that matched and computed nothing produces a
/// credit of zero. Both end at "nothing was paid" and they are different facts, so the flag is
/// carried rather than inferred.
/// </summary>
public sealed record RuleCalculationTrace
{
    /// <summary>
    /// The shape version of this document, written into every persisted trace.
    ///
    /// ★★ IT IS DECLARED, NOT SNIFFED. <c>RuleSnapshotJsonConverter</c> infers a shape by probing for
    /// properties, which works only while the shapes happen to differ and turns every future change
    /// into an archaeology problem. The precedent to follow is <c>Cap.cs:8</c> / <c>Floor.cs:7</c>:
    /// one integer that says what this document is, so a reader never has to guess.
    ///
    /// ★ AND IT IS WHY THE ENUMS ARE PERSISTED AS TEXT. A compact trace keyed on enum ORDINALS would
    /// tie years of stored history to today's declaration order — reordering <see
    /// cref="RuleCalculationOutcome"/> would silently reinterpret every trace ever written, turning
    /// "the cap was skipped" into "the cap applied" with nothing to notice it by.
    /// </summary>
    public int _schema { get; init; } = 1;

    public required bool CreditGenerated { get; init; }

    /// <summary>Null when <see cref="CreditGenerated"/> is false — there is no amount, not an amount of zero.</summary>
    public Money? Commission { get; init; }

    /// <summary>The steps in the order the engine ran them, which is not the order anyone would guess.</summary>
    public required IReadOnlyList<RuleCalculationStep> Steps { get; init; }
}
