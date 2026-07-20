using FluentAssertions;
using Wasnie.Application.Compensation.Common;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// Guards the shared rule used by both CreateQuotaHandler and UpdateQuotaHandler: a quota period
/// must be fully CONTAINED within the plan's effective period. Containment, not mere intersection —
/// a quota that spills outside the plan window measures attainment against a window in which no
/// transaction can be credited. Money-adjacent: a misaligned quota corrupts attainment.
/// </summary>
public sealed class QuotaPeriodGuardTests
{
    // Plan effective period used across cases: 2025-01-01 .. 2025-12-31.
    private static readonly DateRange PlanPeriod =
        DateRange.Of(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));

    [Theory]
    // Fully inside the plan window → valid.
    [InlineData("2025-04-01", "2025-06-30", true)]
    // Exactly equal to the plan window (boundaries inclusive) → valid.
    [InlineData("2025-01-01", "2025-12-31", true)]
    // Single-day at the plan start → valid.
    [InlineData("2025-01-01", "2025-01-01", true)]
    // Starts before the plan window → invalid.
    [InlineData("2024-12-01", "2025-03-31", false)]
    // Ends after the plan window → invalid.
    [InlineData("2025-10-01", "2026-03-31", false)]
    // Partial overlap (starts inside, ends outside) → invalid (must be contained, not intersect).
    [InlineData("2025-06-01", "2026-06-30", false)]
    // Entirely outside the plan window → invalid.
    [InlineData("2026-01-01", "2026-12-31", false)]
    public void Validate_ReturnsErrorOnlyWhenPeriodNotContained(string start, string end, bool expectedValid)
    {
        var periodStart = DateOnly.Parse(start);
        var periodEnd = DateOnly.Parse(end);

        var error = QuotaPeriodGuard.Validate(PlanPeriod, periodStart, periodEnd);

        if (expectedValid)
            error.Should().BeNull();
        else
            error.Should().Be(QuotaPeriodGuard.PeriodOutsidePlanMessage);
    }
}
