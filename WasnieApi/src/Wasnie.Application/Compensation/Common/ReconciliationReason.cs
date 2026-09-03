namespace Wasnie.Application.Compensation.Common;

/// <summary>
/// The complete vocabulary of "why this money could not be paid", as CODES.
///
/// ★★ IT BORROWS, IT DOES NOT REDECLARE. The three unprocessable-pending codes and the ambiguity
/// code are the constants their own specs already publish, and the three engine codes are the names
/// of <c>RateRefusalReason</c> members as the trace and <c>Credits.RateRefusal</c> spell them. A
/// second literal "NoPayee" typed here would agree with the dashboard's until somebody renamed one,
/// and the failure would be silent: a reason filter that quietly matches nothing.
///
/// ★ CODES, NEVER PROSE (§C1). Nothing here is a sentence. The screen owns the wording and the
/// language; an engine or a query that emits phrases has to be redeployed to fix a translation.
///
/// ★ THE FRONT WHITELISTS THESE, it does not interpolate them (§C2). <see cref="All"/> is the list a
/// filter dropdown offers; anything outside it that ever reaches a screen must render as the generic
/// fallback rather than as a raw identifier.
/// </summary>
public static class ReconciliationReason
{
    // ── From the engine's trace, via the queryable Credits.RateRefusal column (KAN-26/KAN-28 A) ──

    /// <summary>An attainment rule whose payee has no quota in effect. KAN-26 tanda 2.</summary>
    public const string NoQuotaInEffect = nameof(NoQuotaInEffect);

    /// <summary>The attainment ladder states no rate for this ratio. KAN-26 tanda 3.</summary>
    public const string NoMatchingBracket = nameof(NoMatchingBracket);

    /// <summary>The ladder priced only part of the sale, so the engine refused it whole. KAN-26 tanda 3.</summary>
    public const string AmountOutsideTable = nameof(AmountOutsideTable);

    // ── From UnprocessablePendingSpec — the SAME constants the dashboard card counts ──

    public const string NoPayee = UnprocessablePendingSpec.NoPayeeReason;
    public const string CurrencyMismatch = UnprocessablePendingSpec.CurrencyMismatchReason;
    public const string NoActiveAssignment = UnprocessablePendingSpec.NoActiveAssignmentReason;

    // ── From AmbiguousAttributionSpec — likewise its own constant ──

    public const string AmbiguousAttribution = AmbiguousAttributionSpec.Reason;

    /// <summary>
    /// A sale with everything it needs in order to pay, carrying no credit. KAN-50.
    ///
    /// ★ THE COMPLEMENT OF THE THREE ABOVE. Those name what is MISSING; this one names a transaction
    /// that is missing nothing and still has no money against it — the state that had no name, and
    /// therefore no screen.
    /// </summary>
    public const string ProcessableWithoutCredit = ProcessableWithoutCreditSpec.Reason;

    // ── Surfaced by the dashboard panel, given codes here for the first time ──

    /// <summary>A deal left closed-won after its commission was calculated or paid.</summary>
    public const string DealLost = nameof(DealLost);

    /// <summary>A deal changed in the CRM after its commission was calculated or paid.</summary>
    public const string CrmDrift = nameof(CrmDrift);

    /// <summary>
    /// An Active plan whose every rule has been stopped: still ingesting sales, paying nothing.
    ///
    /// ★ NOT a <c>PayoutSkipReason</c>. The ticket anticipated that this case has no skip reason of
    /// its own in the engine, and it still does not — this code names the condition the dashboard
    /// panel already detects, and nothing in the engine was invented to produce it.
    /// </summary>
    public const string PlanHasNoActiveRules = nameof(PlanHasNoActiveRules);

    /// <summary>Every code, for the filter dropdown and the front's whitelist.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        NoQuotaInEffect,
        NoMatchingBracket,
        AmountOutsideTable,
        NoPayee,
        CurrencyMismatch,
        NoActiveAssignment,
        AmbiguousAttribution,
        ProcessableWithoutCredit,
        DealLost,
        CrmDrift,
        PlanHasNoActiveRules,
    ];

    public static bool IsKnown(string? code) => code is not null && All.Contains(code);
}
