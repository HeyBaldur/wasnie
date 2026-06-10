using FluentAssertions;
using Wasnie.Application.Common.Helpers;

namespace Wasnie.UnitTests.Application;

public sealed class PeriodHelperPriorPeriodTests
{
    private static readonly DateOnly Today = new(2026, 6, 10);

    // ── ComputePriorPeriodRange ─────────────────────────────────────────────

    [Fact]
    public void ThisMonth_PriorPeriod_IsLastMonth()
    {
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("this-month", Today);
        from.Should().Be(new DateOnly(2026, 5, 1));
        to.Should().Be(new DateOnly(2026, 5, 31));
    }

    [Fact]
    public void ActiveAlias_PriorPeriod_IsLastMonth()
    {
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("active", Today);
        from.Should().Be(new DateOnly(2026, 5, 1));
        to.Should().Be(new DateOnly(2026, 5, 31));
    }

    [Fact]
    public void NullPeriod_DefaultsToThisMonthPrior()
    {
        var (from, to) = PeriodHelper.ComputePriorPeriodRange(null, Today);
        from.Should().Be(new DateOnly(2026, 5, 1));
        to.Should().Be(new DateOnly(2026, 5, 31));
    }

    [Fact]
    public void LastMonth_PriorPeriod_IsTwoMonthsAgo()
    {
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("last-month", Today);
        from.Should().Be(new DateOnly(2026, 4, 1));
        to.Should().Be(new DateOnly(2026, 4, 30));
    }

    [Fact]
    public void Ytd_PriorPeriod_IsSameRangeLastYear()
    {
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("ytd", Today);
        from.Should().Be(new DateOnly(2025, 1, 1));
        to.Should().Be(new DateOnly(2025, 6, 10));
    }

    [Fact]
    public void AllTime_PriorPeriod_IsNull()
    {
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("all-time", Today);
        from.Should().BeNull();
        to.Should().BeNull();
    }

    [Fact]
    public void ThisMonth_OnJanuary_PriorIsDecemberPreviousYear()
    {
        var jan10 = new DateOnly(2026, 1, 10);
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("this-month", jan10);
        from.Should().Be(new DateOnly(2025, 12, 1));
        to.Should().Be(new DateOnly(2025, 12, 31));
    }

    // ── GetPeriodLabel ──────────────────────────────────────────────────────

    [Fact]
    public void GetPeriodLabel_ThisMonth_ReturnsMonthYear()
    {
        PeriodHelper.GetPeriodLabel("this-month", Today).Should().Be("June 2026");
    }

    [Fact]
    public void GetPeriodLabel_LastMonth_ReturnsPreviousMonthYear()
    {
        PeriodHelper.GetPeriodLabel("last-month", Today).Should().Be("May 2026");
    }

    [Fact]
    public void GetPeriodLabel_Ytd_ReturnsYtdYear()
    {
        PeriodHelper.GetPeriodLabel("ytd", Today).Should().Be("YTD 2026");
    }

    [Fact]
    public void GetPeriodLabel_AllTime_ReturnsAllTime()
    {
        PeriodHelper.GetPeriodLabel("all-time", Today).Should().Be("All time");
    }

    // ── GetPriorPeriodLabel ─────────────────────────────────────────────────

    [Fact]
    public void GetPriorPeriodLabel_ThisMonth_ReturnsLastMonth()
    {
        PeriodHelper.GetPriorPeriodLabel("this-month", Today).Should().Be("May 2026");
    }

    [Fact]
    public void GetPriorPeriodLabel_LastMonth_ReturnsTwoMonthsAgo()
    {
        PeriodHelper.GetPriorPeriodLabel("last-month", Today).Should().Be("April 2026");
    }

    [Fact]
    public void GetPriorPeriodLabel_Ytd_ReturnsPriorYear()
    {
        PeriodHelper.GetPriorPeriodLabel("ytd", Today).Should().Be("YTD 2025");
    }

    [Fact]
    public void GetPriorPeriodLabel_AllTime_ReturnsEmpty()
    {
        PeriodHelper.GetPriorPeriodLabel("all-time", Today).Should().Be(string.Empty);
    }
}
