using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.DTOs;

/// <summary>
/// Why a rule could not be simulated. ★ A CODE, NOT A SENTENCE — the reason is shown in three
/// languages, and an engine that emits prose has to be redeployed to fix a translation.
/// </summary>
public enum RuleSimulationBlocker
{
    None = 0,

    /// <summary>
    /// ★ An attainment (bracket) table cannot be evaluated without knowing how much of their quota
    /// the rep has reached. The engine's own default for that is 1.0 — a rep at full quota — so
    /// simulating anyway would not fail loudly: it would quietly report one particular rep's
    /// commission as if it were everybody's.
    /// </summary>
    AttainmentContextRequired = 1,

    /// <summary>
    /// Split-at-quota needs the revenue already booked in the period and the quota target. Without
    /// both, the engine's answer is zero — which is a real outcome for a rep with no quota, and a
    /// meaningless one for a preview.
    /// </summary>
    SplitQuotaContextRequired = 2,
}

/// <summary>One step of the cascade, exactly as the engine emitted it.</summary>
public sealed record RuleSimulationStepDto(
    RuleCalculationComponent Component,
    RuleCalculationOutcome Outcome,
    decimal? InputAmount,
    decimal? OutputAmount,
    decimal? Operand,
    decimal? ThresholdAmount,
    RateTableType? RateTable,
    AttainmentSource? AttainmentSource,
    IReadOnlyList<RuleSimulationTierDto>? Tiers);

public sealed record RuleSimulationTierDto(
    decimal From,
    decimal? To,
    decimal Rate,
    decimal Portion,
    decimal Amount);

/// <summary>
/// What one hypothetical transaction would earn under one rule.
///
/// ★ THE SCOPE IS PART OF THE ANSWER. This is one rule against one transaction — not a payout. A
/// real payout runs several rules, applies quota context and can carry clawback, so a reader who
/// takes this figure as their pay is being misled by a number that is individually correct.
/// </summary>
public sealed record RuleSimulationDto(
    bool Simulated,
    RuleSimulationBlocker Blocker,
    bool CreditGenerated,
    decimal? CommissionAmount,
    string Currency,
    IReadOnlyList<RuleSimulationStepDto> Steps);

/// <summary>
/// One rule's outcome inside a whole-plan simulation.
///
/// ★ IT NAMES ITSELF. When several results travel together, an entry that does not carry its own
/// rule's identity is an entry a reader can attach to the wrong rule — which is precisely the mistake
/// this shape exists to make impossible.
/// </summary>
public sealed record PlanRuleSimulationDto(
    Guid RuleId,
    string RuleName,
    int SortOrder,
    bool Simulated,
    RuleSimulationBlocker Blocker,
    bool CreditGenerated,
    decimal? CommissionAmount,
    IReadOnlyList<RuleSimulationStepDto> Steps);

/// <summary>
/// Every active rule of a plan against one hypothetical transaction.
///
/// ★★ THERE IS NO TOTAL HERE, AND THAT IS DELIBERATE. Adding the rules up would look like the payout
/// and would not be one: a real payout resolves which plan applies, can involve quota context this
/// call refused to guess at, and can carry clawback. A sum printed beside those caveats is the number
/// people would quote.
/// </summary>
public sealed record PlanSimulationDto(
    Guid PlanId,
    string PlanName,
    string Currency,
    decimal Amount,
    int Quantity,
    IReadOnlyList<PlanRuleSimulationDto> Rules);
