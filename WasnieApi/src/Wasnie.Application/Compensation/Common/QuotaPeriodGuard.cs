using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Application.Compensation.Common;

/// <summary>
/// Single source of truth for the rule: a Quota's period must be fully CONTAINED within
/// its Plan's effective period. Attainment is measured against the quota period, and
/// transactions are only credited to a (payee, plan) within the assignment window — which
/// this codebase forces to equal the plan's effective period (see AssignPlanToPayeeHandler).
/// A quota period that spills outside the plan period therefore measures attainment against
/// a window in which no transaction can ever be credited → misleading/incorrect attainment.
///
/// Used by BOTH CreateQuotaHandler and UpdateQuotaHandler so the rule cannot be bypassed
/// through the update "back door". The backend is the source of truth even though the UI
/// validates the same thing client-side.
/// </summary>
public static class QuotaPeriodGuard
{
    public const string PeriodOutsidePlanMessage =
        "The quota period must fall within the selected plan's effective period.";

    /// <summary>
    /// Returns an error message when the quota period is NOT fully contained within the
    /// plan's effective period (partial overlap is rejected — containment must be total);
    /// otherwise returns null.
    /// </summary>
    public static string? Validate(DateRange planEffectivePeriod, DateOnly periodStart, DateOnly periodEnd)
    {
        if (periodStart < planEffectivePeriod.Start || periodEnd > planEffectivePeriod.End)
            return PeriodOutsidePlanMessage;

        return null;
    }
}
