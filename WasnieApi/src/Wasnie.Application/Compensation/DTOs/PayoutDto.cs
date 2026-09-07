namespace Wasnie.Application.Compensation.DTOs;

public sealed record OverlappingPayoutDto(
    Guid Id,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    string PlanName,
    decimal TotalCommissionAmount,
    string TotalCommissionCurrency);

public sealed record PayoutListItemDto(
    Guid Id,
    Guid PayeeId,
    string PayeeName,
    string PayeeCode,
    Guid PlanId,
    string PlanName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TotalCommissionAmount,
    string TotalCommissionCurrency,
    string Status,
    DateTimeOffset CalculatedAt,
    string CalculatedBy,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    // When the money actually left. Null unless Status == Paid. Appended last so existing positional
    // construction sites keep compiling.
    DateTimeOffset? PaidAt = null);

public sealed record PayoutDto(
    Guid Id,
    Guid TenantId,
    Guid PayeeId,
    string PayeeName,
    string PayeeCode,
    Guid PlanId,
    string PlanName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TotalCommissionAmount,
    string TotalCommissionCurrency,
    string Status,
    DateTimeOffset CalculatedAt,
    string CalculatedBy,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    IReadOnlyList<PayoutLineDto> Lines);

public sealed record PayoutLineDto(
    Guid Id,
    Guid CreditId,
    Guid RuleId,
    string RuleName,
    decimal BaseAmount,
    string BaseCurrency,
    decimal CommissionAmount,
    string CommissionCurrency,
    // Source transaction — null only if data is missing (should not occur in normal operation)
    Guid? TransactionId,
    string? TransactionReference,
    // Human-readable label of the source sale — the audit trail from a commission back to its deal.
    string? TransactionDescription,
    string? TransactionExternalId,
    DateOnly? TransactionDate,
    decimal? TransactionAmount,
    string? TransactionCurrency,
    // Calculation explanation — null if credit data unavailable
    LineCalculationDto? Calculation,

    /// <summary>
    /// What happened to this line's money: paid here, paid by another payout, or not paid at all.
    /// See <see cref="PayoutLinePaymentState"/>.
    /// </summary>
    PayoutLinePaymentState PaymentState = PayoutLinePaymentState.Unpaid,

    /// <summary>
    /// Which payout paid it, when that payout is not this one — the duplicate that makes this payout
    /// unpayable and is the reason the discard exists.
    ///
    /// ★★ IT IS THE SAME FACT THE PAYMENT GUARD USES, SURFACED. The server has always known which
    /// credits are already consumed — it is what blocks the payment and what decides whether a payout
    /// can be discarded. The statement did not show it, so the only way to find out whether a stuck
    /// payout was wholly or partly duplicated was to press Discard and read the refusal. Answering it
    /// on the line itself turns trial and error into a glance.
    ///
    /// ★ THE PERIOD, NOT JUST THE ID. "Already paid in 7839C4D2" tells the reader nothing; the period
    /// of the payout that paid it is what shows the overlap that caused the duplicate. Same shape as
    /// PaymentConflictItem, which the double-pay banner already speaks.
    /// </summary>
    Guid? PaidInPayoutId = null,
    DateOnly? PaidInPayoutPeriodStart = null,
    DateOnly? PaidInPayoutPeriodEnd = null);

// ── Calculation explanation DTOs ──────────────────────────────────────────────

public sealed record LineCalculationDto(
    int PlanVersion,
    DateTimeOffset FrozenAt,
    RateTableDto RateTable,
    TriggerDto Trigger,
    IReadOnlyList<ModifierApplicationDto> Modifiers);

/// <param name="MeasurementBase">
/// What the rate is applied TO: <c>TransactionAmount</c> or <c>TransactionQuantity</c>.
///
/// ★★ WITHOUT IT A RATE CANNOT BE RENDERED, AND THE SCREEN GUESSED. The stored value is a bare
/// decimal: 0.05 against an AMOUNT is five per cent; 5.00 against a QUANTITY is five euros per unit.
/// The client had only the number, assumed every flat rate was a percentage, multiplied by 100 and
/// appended "%" — so a real rule of €5 per unit rendered as "500% flat" on a payout statement.
///
/// ★ IT IS THE FACT, NOT THE PRESENTATION. This says what the rate applies to; how to write that
/// for a reader is the client's business, in the reader's language and currency format. Sending a
/// pre-formatted string would move presentation into the API and freeze it in one language.
/// </param>
public sealed record RateTableDto(
    string Type,
    decimal? FlatRate,
    IReadOnlyList<RateTierDto>? Tiers,
    IReadOnlyList<AttainmentTierDto>? AttainmentTiers,
    string MeasurementBase = "TransactionAmount");

public sealed record RateTierDto(decimal From, decimal? To, decimal Rate);

public sealed record AttainmentTierDto(decimal AttainmentFrom, decimal? AttainmentTo, decimal Rate);

public sealed record TriggerDto(
    bool IsAlways,
    string LogicalOperator,
    IReadOnlyList<ConditionDto> Conditions);

public sealed record ConditionDto(string Field, string Operator, string Value);

public sealed record ModifierApplicationDto(
    string ModifierName,
    decimal FactorApplied,
    decimal AmountBefore,
    string AmountBeforeCurrency,
    decimal AmountAfter,
    string AmountAfterCurrency);

/// <summary>
/// What happened to one commission line's money.
///
/// ★★ THREE STATES, BECAUSE TWO WERE A LIE. The first cut carried only "paid somewhere else" as a
/// nullable id, and the screen read its absence as "not paid" — so a payout that had itself paid
/// these very credits showed every one of its lines as unpaid, contradicting the Transactions list
/// three clicks away. A null meant two different things at once (§B3: one field, one meaning), and
/// the fix is not a second boolean beside it but a state that can only ever be one of these.
/// </summary>
public enum PayoutLinePaymentState
{
    /// <summary>Nobody has paid this commission yet.</summary>
    Unpaid = 0,

    /// <summary>Paid by the payout being looked at — the ordinary, correct outcome.</summary>
    PaidByThisPayout = 1,

    /// <summary>
    /// Paid by a DIFFERENT payout: the duplicate that makes this payout unpayable and is the reason
    /// the discard exists. <c>PaidInPayoutPeriodStart/End</c> say which one.
    /// </summary>
    PaidByAnotherPayout = 2,
}
