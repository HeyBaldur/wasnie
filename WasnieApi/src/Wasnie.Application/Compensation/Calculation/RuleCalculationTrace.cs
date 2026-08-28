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
    public required bool CreditGenerated { get; init; }

    /// <summary>Null when <see cref="CreditGenerated"/> is false — there is no amount, not an amount of zero.</summary>
    public Money? Commission { get; init; }

    /// <summary>The steps in the order the engine ran them, which is not the order anyone would guess.</summary>
    public required IReadOnlyList<RuleCalculationStep> Steps { get; init; }
}
