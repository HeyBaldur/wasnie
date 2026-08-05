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
    /// Returns the [From, To] range the current period is compared against for the Banda 3 trend.
    /// Returns (null, null) for "all-time" or unknown.
    ///
    /// ── THE INVARIANT — ONE RULE FOR EVERY PERIOD ────────────────────────────────────────────
    ///   · A period still RUNNING is compared against the EQUIVALENT ELAPSED SLICE of the previous
    ///     one — the same number of days — clamped to that period's end.
    ///   · A period already CLOSED is compared against the whole of the previous one.
    ///
    /// Both halves are computed here, generically, for every key. Nothing is special-cased per period,
    /// and that is the point: this used to be a per-key switch in which "this-month" alone compared a
    /// few elapsed days against a FULL previous month. On 5 August that read five days of August against
    /// all thirty-one of July and printed roughly -85% in red, a collapse that never happened — and
    /// switching the filter to This Quarter silently changed the formula under the user.
    ///
    /// If a new period key is added, it inherits the correct rule automatically: whether it is running
    /// is derived from its own range, not declared by hand.
    /// </summary>
    public static (DateOnly? From, DateOnly? To) ComputePriorPeriodRange(string? period, DateOnly today)
    {
        var normalized = (period ?? "this-month").ToLowerInvariant();

        var (from, to) = ComputeDateRange(normalized, today);
        var previous = PreviousPeriodBounds(normalized, today);

        // "all-time" and unknown keys have no bounded range, so there is nothing to compare against.
        if (from is null || to is null || previous is null) return (null, null);

        var (priorStart, priorEnd) = previous.Value;

        // THE DISCRIMINATOR, derived rather than declared: a period is still running exactly when its
        // range ends today. Every closed period ends on a fixed past date.
        var isRunning = to.Value == today;
        if (!isRunning) return (priorStart, priorEnd);

        // Equal elapsed days. Clamped because periods differ in length — 31 days of March have no
        // counterpart in February, and Q1 (90/91 days) is shorter than Q3 and Q4 (92). Without the
        // clamp the "previous" window would spill forward into the current period and count days twice.
        //
        // Clamping is also what makes this leap-day safe: AddDays can never build an invalid date,
        // whereas reconstructing the same calendar day a year earlier throws every 29 February.
        var elapsedDays = to.Value.DayNumber - from.Value.DayNumber;
        var priorTo = priorStart.AddDays(elapsedDays);
        if (priorTo > priorEnd) priorTo = priorEnd;

        return (priorStart, priorTo);
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

    /// <summary>
    /// Labels for the TREND comparison specifically — which is not the same question as "what period am
    /// I looking at", and must not reuse those labels.
    ///
    /// A RUNNING period is compared slice against slice, so both sides are labelled with their actual
    /// day ranges. Naming them after the whole period instead produces a flat contradiction inside the
    /// same dashboard: on 5 August the trend card would read "Prior: July 2026 — €0" while the Last Month
    /// filter reports July at €4,939.41. The number is right; the name is a lie, and the user has no way
    /// to tell which screen to believe.
    ///
    /// A CLOSED period is compared whole against whole, so the period names are exact and are kept —
    /// "July 2026 vs June 2026" reads better than spelling out both date ranges.
    /// </summary>
    public static (string Current, string Prior) GetTrendLabels(string? period, DateOnly today)
    {
        var normalized = (period ?? "this-month").ToLowerInvariant();
        var (from, to) = ComputeDateRange(normalized, today);
        var (priorFrom, priorTo) = ComputePriorPeriodRange(normalized, today);

        // Closed periods, and anything unbounded, keep the period names.
        if (from is not { } start || to is not { } end || end != today
            || priorFrom is not { } priorStart || priorTo is not { } priorEnd)
        {
            return (GetPeriodLabel(normalized, today), GetPriorPeriodLabel(normalized, today));
        }

        return (FormatRange(start, end), FormatRange(priorStart, priorEnd));
    }

    /// <summary>Compact inclusive range, e.g. "1–5 Aug 2026", "1 Apr – 6 May 2026", "1 Jan 2026".</summary>
    private static string FormatRange(DateOnly from, DateOnly to)
    {
        var ci = CultureInfo.InvariantCulture;
        if (from == to)
            return from.ToString("d MMM yyyy", ci);

        if (from.Year == to.Year && from.Month == to.Month)
            return $"{from.Day}–{to.Day} {to.ToString("MMM yyyy", ci)}";

        if (from.Year == to.Year)
            return $"{from.ToString("d MMM", ci)} – {to.ToString("d MMM yyyy", ci)}";

        return $"{from.ToString("d MMM yyyy", ci)} – {to.ToString("d MMM yyyy", ci)}";
    }
}
