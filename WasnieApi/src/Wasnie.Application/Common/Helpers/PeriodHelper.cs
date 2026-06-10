using System.Globalization;

namespace Wasnie.Application.Common.Helpers;

public static class PeriodHelper
{
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

            "ytd" =>
                (new DateOnly(today.Year, 1, 1), today),

            // "all-time", "all", unknown → no date filter
            _ => (null, null),
        };

    /// <summary>
    /// Returns the [From, To] range of the period immediately prior to the given one.
    /// Used for Banda 3 trend comparison. Returns (null, null) for "all-time" or unknown.
    /// </summary>
    public static (DateOnly? From, DateOnly? To) ComputePriorPeriodRange(string? period, DateOnly today)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        return (period ?? "this-month").ToLowerInvariant() switch
        {
            "this-month" or "active" =>
                (monthStart.AddMonths(-1), monthStart.AddDays(-1)),

            "last-month" =>
                (monthStart.AddMonths(-2), monthStart.AddMonths(-1).AddDays(-1)),

            // Prior YTD = same day-range in the previous calendar year
            "ytd" =>
                (new DateOnly(today.Year - 1, 1, 1),
                 new DateOnly(today.Year - 1, today.Month, today.Day)),

            _ => (null, null),
        };
    }

    /// <summary>Returns a human-readable label for the current period (e.g. "June 2026", "YTD 2026").</summary>
    public static string GetPeriodLabel(string? period, DateOnly today) =>
        (period ?? "this-month").ToLowerInvariant() switch
        {
            "this-month" or "active" =>
                today.ToString("MMMM yyyy", CultureInfo.InvariantCulture),

            "last-month" =>
                new DateOnly(today.Year, today.Month, 1).AddMonths(-1)
                    .ToString("MMMM yyyy", CultureInfo.InvariantCulture),

            "ytd" => $"YTD {today.Year}",

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

            "ytd" => $"YTD {today.Year - 1}",

            _ => string.Empty,
        };
}
