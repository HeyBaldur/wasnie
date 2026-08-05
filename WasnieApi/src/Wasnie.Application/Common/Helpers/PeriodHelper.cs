using System.Globalization;

namespace Wasnie.Application.Common.Helpers;

public static class PeriodHelper
{
    // Quarters are CALENDAR quarters (Q1 Jan–Mar … Q4 Oct–Dec). Wasnie has no fiscal-year concept
    // anywhere in the domain — PlanPeriodType.Quarterly carries no year-start offset and no tenant
    // setting defines one — so there is nothing to make configurable yet. If a fiscal year is ever
    // introduced, it belongs here and in the two Quarter helpers below, and nowhere else.
    private static DateOnly QuarterStart(DateOnly d) => new(d.Year, ((d.Month - 1) / 3) * 3 + 1, 1);

    private static DateOnly QuarterEnd(DateOnly quarterStart) => quarterStart.AddMonths(3).AddDays(-1);

    private static int QuarterNumber(DateOnly d) => (d.Month - 1) / 3 + 1;

    /// <summary>
    /// Computes the inclusive [From, To] date range for a period selector value.
    /// Used uniformly by all dashboard card endpoints so every section scopes data
    /// to the same date window.
    /// </summary>
    public static (DateOnly? From, DateOnly? To) ComputeDateRange(string? period, DateOnly today) =>
        (period ?? "this-month").ToLowerInvariant() switch
        {
            "this-month" or "active" =>
                (new DateOnly(today.Year, today.Month, 1), today),

            "last-month" =>
                (new DateOnly(today.Year, today.Month, 1).AddMonths(-1),
                 new DateOnly(today.Year, today.Month, 1).AddDays(-1)),

            // Quarter to date: the quarter is still running, so it ends today, not at the quarter end.
            "this-quarter" =>
                (QuarterStart(today), today),

            "last-quarter" =>
                (QuarterStart(today).AddMonths(-3),
                 QuarterStart(today).AddDays(-1)),

            "ytd" =>
                (new DateOnly(today.Year, 1, 1), today),

            // A closed year, in full — the comparison baseline for year-over-year.
            "last-year" =>
                (new DateOnly(today.Year - 1, 1, 1), new DateOnly(today.Year - 1, 12, 31)),

            // "all-time", "all", unknown → no date filter.
            //
            // The dashboard no longer OFFERS all-time (an unbounded scan, and a total that mixes years
            // under different plans). This fallback stays because the payouts list, the pay-runs list and
            // the payee detail screen still use the key, and because an unrecognised value must degrade
            // to "no filter" rather than throw.
            _ => (null, null),
        };

    /// <summary>
    /// Whether the period is still RUNNING — derived from its own range rather than declared in a list,
    /// so a period key added later classifies itself. A running period's range ends today; every closed
    /// period ends on a fixed past date. "all-time" and unknown keys are unbounded and never running.
    ///
    /// This drives PRESENTATION only: a running period is shown as PACING (progress against the previous
    /// period's total), a closed one as a percentage change. Both compare against the same window.
    /// </summary>
    public static bool IsRunningPeriod(string? period, DateOnly today)
    {
        var (_, to) = ComputeDateRange(period, today);
        return to.HasValue && to.Value == today;
    }

    /// <summary>
    /// Returns the FULL [From, To] range of the period preceding the given one — the comparison window
    /// for the Banda 3 band. Returns (null, null) for "all-time" or unknown.
    ///
    /// ONE WINDOW FOR EVERY PERIOD: always the whole previous period, running or closed. A running period
    /// used to be compared against the equivalent elapsed slice (five days of August against five of
    /// July). That was arithmetically honest but commercially useless — B2B payments cluster at the end
    /// of a period, so the first days of a month read €0 against €0 and told nobody anything.
    ///
    /// The previous period's TOTAL is instead used as a baseline the running period is pacing towards.
    /// That makes the comparison meaningful from day one, and it is why a running period must NOT be
    /// rendered as a percentage change: €500 of August against all €4,939 of July is -89.9% by that
    /// formula, a red collapse arrow every first of the month. See <see cref="IsRunningPeriod"/> — the
    /// handler switches the PRESENTATION on it, never the window.
    /// </summary>
    public static (DateOnly? From, DateOnly? To) ComputePriorPeriodRange(string? period, DateOnly today)
    {
        var normalized = (period ?? "this-month").ToLowerInvariant();
        return PreviousPeriodBounds(normalized, today) is { } previous
            ? (previous.Start, previous.End)
            : (null, null);
    }

    /// <summary>
    /// The FULL bounds of the period preceding the given one — before any running/closed adjustment.
    /// Kept separate from the invariant above so the comparison rule lives in exactly one place.
    /// </summary>
    private static (DateOnly Start, DateOnly End)? PreviousPeriodBounds(string normalizedPeriod, DateOnly today)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var quarterStart = QuarterStart(today);

        return normalizedPeriod switch
        {
            "this-month" or "active" =>
                (monthStart.AddMonths(-1), monthStart.AddDays(-1)),

            "last-month" =>
                (monthStart.AddMonths(-2), monthStart.AddMonths(-1).AddDays(-1)),

            "this-quarter" =>
                (quarterStart.AddMonths(-3), quarterStart.AddDays(-1)),

            "last-quarter" =>
                (quarterStart.AddMonths(-6), quarterStart.AddMonths(-3).AddDays(-1)),

            "ytd" =>
                (new DateOnly(today.Year - 1, 1, 1), new DateOnly(today.Year - 1, 12, 31)),

            "last-year" =>
                (new DateOnly(today.Year - 2, 1, 1), new DateOnly(today.Year - 2, 12, 31)),

            _ => null,
        };
    }

    /// <summary>Returns a human-readable label for the current period (e.g. "June 2026", "Q3 2026").</summary>
    public static string GetPeriodLabel(string? period, DateOnly today) =>
        (period ?? "this-month").ToLowerInvariant() switch
        {
            "this-month" or "active" =>
                today.ToString("MMMM yyyy", CultureInfo.InvariantCulture),

            "last-month" =>
                new DateOnly(today.Year, today.Month, 1).AddMonths(-1)
                    .ToString("MMMM yyyy", CultureInfo.InvariantCulture),

            "this-quarter" => $"Q{QuarterNumber(today)} {today.Year}",

            "last-quarter" => QuarterLabel(QuarterStart(today).AddMonths(-3)),

            "ytd" => $"YTD {today.Year}",

            "last-year" => $"{today.Year - 1}",

            _ => "All time",
        };

    /// <summary>Returns a human-readable label for the prior period (used in trend tooltips).</summary>
    public static string GetPriorPeriodLabel(string? period, DateOnly today) =>
        (period ?? "this-month").ToLowerInvariant() switch
        {
            "this-month" or "active" =>
                new DateOnly(today.Year, today.Month, 1).AddMonths(-1)
                    .ToString("MMMM yyyy", CultureInfo.InvariantCulture),

            "last-month" =>
                new DateOnly(today.Year, today.Month, 1).AddMonths(-2)
                    .ToString("MMMM yyyy", CultureInfo.InvariantCulture),

            "this-quarter" => QuarterLabel(QuarterStart(today).AddMonths(-3)),

            "last-quarter" => QuarterLabel(QuarterStart(today).AddMonths(-6)),

            "ytd" => $"YTD {today.Year - 1}",

            "last-year" => $"{today.Year - 2}",

            _ => string.Empty,
        };

    private static string QuarterLabel(DateOnly quarterStart) =>
        $"Q{QuarterNumber(quarterStart)} {quarterStart.Year}";

}
