namespace Wasnie.Application.Common.Helpers;

/// <summary>
/// Range semantics for the dashboard, which no longer filters by a named period.
///
/// ★ SEPARATE FROM <see cref="PeriodHelper"/> ON PURPOSE. That helper turns a preset key into a range
/// and is still the single source of truth for the five screens that keep the presets (the payouts
/// list, pay runs, the payee detail, the ledger summary, quotas and assignments). This one starts from
/// a range the user picked, so there is no key to interpret — merging the two would give
/// <c>PeriodHelper</c> a second job and drag those five screens into this change.
/// </summary>
public static class DashboardRangeHelper
{
    /// <summary>
    /// The range the dashboard opens on: the WHOLE current month, first to last day.
    ///
    /// Note it ends on the last day of the month, which is usually in the FUTURE. That is deliberate —
    /// the user asked for "this month", not "this month so far" — and it is why
    /// <see cref="IsRunningRange"/> cannot test for equality with today.
    /// </summary>
    public static (DateOnly From, DateOnly To) DefaultRange(DateOnly today)
    {
        var first = new DateOnly(today.Year, today.Month, 1);
        return (first, first.AddMonths(1).AddDays(-1));
    }

    /// <summary>
    /// Whether the range is still RUNNING — i.e. it has not finished yet, so the figures inside it are
    /// still moving.
    ///
    /// ★★ THE COMPARISON IS `To >= today`, NOT `To == today`. With presets, a running period always
    /// ended exactly today, so equality was enough. A free range does not: the default range ends on
    /// the last day of the month, which is in the future for all but one day of it. Under the old test
    /// that range would be classified as CLOSED, and the trend band would render eight days of
    /// September against the whole of August as a percentage change — the red collapse arrow every
    /// first of the month that <see cref="PeriodHelper.ComputePriorPeriodRange"/> exists to avoid.
    ///
    /// Still DERIVED from the range itself rather than declared by the caller, so there is no second
    /// place that can disagree about what "running" means.
    /// </summary>
    public static bool IsRunningRange(DateOnly to, DateOnly today) => to >= today;

    /// <summary>
    /// The window the trend band compares against: the one immediately preceding the given range.
    ///
    /// TWO CASES, AND THE SPECIAL ONE IS THE DEFAULT VIEW:
    ///
    /// ★★ A RANGE THAT IS EXACTLY A CALENDAR MONTH COMPARES AGAINST THE WHOLE PREVIOUS CALENDAR MONTH.
    /// The dashboard opens on the current month, and the rest of the product still speaks in months —
    /// the payouts and pay-run screens have a "last month" view. Length-matching September (30 days)
    /// would compare it against 2–31 August, and the dashboard's August figure would then disagree
    /// with the August every other screen reports, for no reason a reader could ever guess. Months are
    /// the one range where a calendar step and a length step differ AND the calendar answer is the one
    /// the reader means.
    ///
    /// ★ EVERY OTHER RANGE IS LENGTH-MATCHED: the same number of days, immediately before. A free
    /// range has no name and no calendar unit, so the only comparison that stays honest for 3 days, 40
    /// days or a quarter is an equally long window. Stepping by calendar months instead would compare
    /// 40 days against 31 and call the difference a trend.
    /// </summary>
    public static (DateOnly From, DateOnly To) PriorRange(DateOnly from, DateOnly to)
    {
        if (IsWholeCalendarMonth(from, to))
        {
            var priorMonthStart = from.AddMonths(-1);
            return (priorMonthStart, from.AddDays(-1));
        }

        var lengthInDays = to.DayNumber - from.DayNumber + 1;
        var priorTo = from.AddDays(-1);
        return (priorTo.AddDays(-(lengthInDays - 1)), priorTo);
    }

    private static bool IsWholeCalendarMonth(DateOnly from, DateOnly to) =>
        from.Day == 1
        && to.Year == from.Year
        && to.Month == from.Month
        && to.Day == DateTime.DaysInMonth(from.Year, from.Month);
}
