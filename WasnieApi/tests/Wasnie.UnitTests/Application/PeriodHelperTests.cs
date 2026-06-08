using FluentAssertions;
using Wasnie.Application.Common.Helpers;

namespace Wasnie.UnitTests.Application;

public sealed class PeriodHelperTests
{
    private static readonly DateOnly Today = new(2026, 6, 8); // fixed "today" for determinism

    [Fact]
    public void ThisMonth_ReturnsFirstOfMonthToToday()
    {
        var (from, to) = PeriodHelper.ComputeDateRange("this-month", Today);

        from.Should().Be(new DateOnly(2026, 6, 1));
        to.Should().Be(Today);
    }

    [Fact]
    public void ActiveLegacyAlias_MapsToThisMonth()
    {
        var (from, to) = PeriodHelper.ComputeDateRange("active", Today);

        from.Should().Be(new DateOnly(2026, 6, 1));
        to.Should().Be(Today);
    }

    [Fact]
    public void NullPeriod_DefaultsToThisMonth()
    {
        var (from, to) = PeriodHelper.ComputeDateRange(null, Today);

        from.Should().Be(new DateOnly(2026, 6, 1));
        to.Should().Be(Today);
    }

    [Fact]
    public void LastMonth_ReturnsPreviousFullCalendarMonth()
    {
        var (from, to) = PeriodHelper.ComputeDateRange("last-month", Today);

        from.Should().Be(new DateOnly(2026, 5, 1));
        to.Should().Be(new DateOnly(2026, 5, 31));
    }

    [Fact]
    public void Ytd_ReturnsJanFirstToToday()
    {
        var (from, to) = PeriodHelper.ComputeDateRange("ytd", Today);

        from.Should().Be(new DateOnly(2026, 1, 1));
        to.Should().Be(Today);
    }

    [Fact]
    public void AllTime_ReturnsBothNull()
    {
        var (from, to) = PeriodHelper.ComputeDateRange("all-time", Today);

        from.Should().BeNull();
        to.Should().BeNull();
    }

    [Fact]
    public void AllLegacyAlias_ReturnsBothNull()
    {
        var (from, to) = PeriodHelper.ComputeDateRange("all", Today);

        from.Should().BeNull();
        to.Should().BeNull();
    }

    [Fact]
    public void UnknownString_ReturnsBothNull()
    {
        var (from, to) = PeriodHelper.ComputeDateRange("bogus-value", Today);

        from.Should().BeNull();
        to.Should().BeNull();
    }

    [Fact]
    public void LastMonth_OnFirstDayOfYear_ReturnsDecemberOfPreviousYear()
    {
        var jan1 = new DateOnly(2026, 1, 1);
        var (from, to) = PeriodHelper.ComputeDateRange("last-month", jan1);

        from.Should().Be(new DateOnly(2025, 12, 1));
        to.Should().Be(new DateOnly(2025, 12, 31));
    }
}
