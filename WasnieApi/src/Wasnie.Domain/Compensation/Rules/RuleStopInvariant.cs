namespace Wasnie.Domain.Compensation.Rules;

/// <summary>
/// The ways an attempt to STOP a live rule can be refused, as codes.
///
/// ★ THE SAME CONTRACT <see cref="RateTableInvariant"/> CARRIES. These strings are an API: the front
/// end matches them against its own EN/ES/PL translations, and renaming one silently degrades the
/// stop dialog to its neutral fallback, which says the rule was not stopped without saying why.
/// Add a code and its three translations in the same change.
///
/// ★ THE REFUSALS MATTER MORE HERE THAN ELSEWHERE. This is the emergency brake of a commission
/// engine. Someone reaches for it because money is going out wrong, and a refusal they cannot read
/// is a refusal they will retry instead of route around.
/// </summary>
public static class RuleStopInvariant
{
    /// <summary>
    /// The rule is already stopped. Checked BEFORE the reason is validated: telling someone to
    /// write a reason for a brake that is already pulled sends them back to a form for nothing.
    /// </summary>
    public const string AlreadyStopped = "RuleAlreadyStopped";

    /// <summary>
    /// No reason was given. ★ THE REASON IS THE POINT OF THE RECORD, NOT PAPERWORK — the stopped
    /// rule outlives everyone who remembers why, and <c>AuditLogs</c> is not a surface anyone reads.
    /// </summary>
    public const string ReasonRequired = "RuleStopReasonRequired";

    /// <summary>The reason exceeds what the column stores. Carries <c>maxLength</c>.</summary>
    public const string ReasonTooLong = "RuleStopReasonTooLong";

    /// <summary>
    /// The plan is not Active, so there is nothing live to stop. Carries <c>status</c>. A Draft's
    /// rules are removed and edited outright; an Archived plan already generates nothing.
    /// </summary>
    public const string PlanNotActive = "RuleStopPlanNotActive";

    /// <summary>No rule with that id belongs to this plan.</summary>
    public const string RuleNotFound = "RuleStopRuleNotFound";
}
