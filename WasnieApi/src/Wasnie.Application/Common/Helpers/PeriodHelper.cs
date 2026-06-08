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
}
