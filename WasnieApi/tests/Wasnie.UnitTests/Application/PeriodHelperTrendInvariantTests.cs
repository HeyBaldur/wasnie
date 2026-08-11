using FluentAssertions;
using Wasnie.Application.Common.Helpers;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// THE COMPARISON RULE — one window for every period, and a presentation switch on top of it:
///
///   · Every period, running or closed, is compared against the WHOLE previous period.
///   · A RUNNING period is then PRESENTED as pacing (progress towards that total).
///   · A CLOSED period is presented as a change percentage.
///
/// A running period used to be compared against the equivalent elapsed slice (five days of August
/// against five of July). That was arithmetically honest and commercially useless: B2B payments cluster
/// at the end of a period, so the opening days of a month read €0 against €0. The baseline is now the
/// previous period's total — which is exactly why a running period must never be rendered as a change
/// percentage: €500 of August against all €4,939 of July is -89.9%, a red collapse every 1st.
///
/// These tests pin both halves: the window, and the running/closed classification that decides the
/// presentation.
/// </summary>
public sealed class PeriodHelperTrendInvariantTests
{
    // Deliberately the date from the bug report: 5 days into August.
    private static readonly DateOnly EarlyAugust = new(2026, 8, 5);

    private static readonly string[] RunningPeriods = ["this-month", "active", "this-quarter", "ytd"];
    private static readonly string[] ClosedPeriods = ["last-month", "last-quarter", "last-year"];

    // ── every period compares against the WHOLE previous period ───────────────

    [Fact]
    public void ThisMonth_ComparesAgainstTheWholePreviousMonth()
    {
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("this-month", EarlyAugust);

        from.Should().Be(new DateOnly(2026, 7, 1));
        to.Should().Be(new DateOnly(2026, 7, 31),
            "the baseline is July's TOTAL, not the slice that matches how far August has run");
    }

    [Fact]
    public void ActiveAlias_FollowsTheSameRuleAsThisMonth()
    {
        PeriodHelper.ComputePriorPeriodRange("active", EarlyAugust)
            .Should().Be(PeriodHelper.ComputePriorPeriodRange("this-month", EarlyAugust));
    }

    [Fact]
    public void EveryPeriod_ComparesAgainstTheFullPrecedingPeriod()
    {
        // The window is now identical in kind for running and closed periods: the previous period, whole.
        // If a future change reintroduces a partial window for one of them, this fails.
        foreach (var period in RunningPeriods.Concat(ClosedPeriods))
        {
            var (priorFrom, priorTo) = PeriodHelper.ComputePriorPeriodRange(period, EarlyAugust);
            priorFrom.Should().NotBeNull($"{period} must have a comparison window");

            // The previous period abuts the current one: it ends the day before the current one starts.
            var (curFrom, _) = PeriodHelper.ComputeDateRange(period, EarlyAugust);
            priorTo!.Value.AddDays(1).Should().Be(curFrom!.Value,
                $"{period} must be compared against the FULL preceding period");
        }
    }

    [Theory]
    [InlineData("this-month", 2026, 7, 1, 2026, 7, 31)]
    [InlineData("last-month", 2026, 6, 1, 2026, 6, 30)]
    [InlineData("this-quarter", 2026, 4, 1, 2026, 6, 30)]
    [InlineData("last-quarter", 2026, 1, 1, 2026, 3, 31)]
    [InlineData("ytd", 2025, 1, 1, 2025, 12, 31)]
    [InlineData("last-year", 2024, 1, 1, 2024, 12, 31)]
    public void ComparisonWindowIsPinnedPerPeriod(
        string period, int fy, int fm, int fd, int ty, int tm, int td)
    {
        var (from, to) = PeriodHelper.ComputePriorPeriodRange(period, EarlyAugust);

        from.Should().Be(new DateOnly(fy, fm, fd));
        to.Should().Be(new DateOnly(ty, tm, td));
    }

    // ── the running/closed split drives PRESENTATION ──────────────────────────

    [Fact]
    public void RunningPeriodsAreClassifiedAsRunning()
    {
        foreach (var period in RunningPeriods)
            PeriodHelper.IsRunningPeriod(period, EarlyAugust).Should().BeTrue(
                $"{period} ends today, so it is still running and must be shown as pacing");
    }

    [Fact]
    public void ClosedPeriodsAreNotClassifiedAsRunning()
    {
        foreach (var period in ClosedPeriods)
            PeriodHelper.IsRunningPeriod(period, EarlyAugust).Should().BeFalse(
                $"{period} ended in the past, so it keeps the change-percentage presentation");
    }

    [Fact]
    public void ClassificationIsDerivedFromTheRange_NotAHandMaintainedList()
    {
        // IsRunningPeriod asks the period's own range whether it ends today. This pins that equivalence
        // so a period key added later cannot land on one side while behaving like the other.
        foreach (var period in RunningPeriods.Concat(ClosedPeriods))
        {
            var (_, to) = PeriodHelper.ComputeDateRange(period, EarlyAugust);
            PeriodHelper.IsRunningPeriod(period, EarlyAugust)
                .Should().Be(to == EarlyAugust, $"{period}");
        }
    }

    [Theory]
    [InlineData("all-time")]
    [InlineData("next-tuesday")]
    public void UnboundedPeriodsAreNeitherRunningNorComparable(string period)
    {
        PeriodHelper.IsRunningPeriod(period, EarlyAugust).Should().BeFalse();

        var (from, to) = PeriodHelper.ComputePriorPeriodRange(period, EarlyAugust);
        from.Should().BeNull();
        to.Should().BeNull();
    }

    // ── no date arithmetic can throw ──────────────────────────────────────────

    [Fact]
    public void LeapDay_IsHandledForEveryPeriod()
    {
        // 29 February has no counterpart in a common year. The window is built from whole-period bounds
        // rather than a reconstructed calendar date, so there is nothing left that can throw.
        var leapDay = new DateOnly(2028, 2, 29);

        foreach (var period in RunningPeriods.Concat(ClosedPeriods))
        {
            var act = () => PeriodHelper.ComputePriorPeriodRange(period, leapDay);
            act.Should().NotThrow($"{period} must resolve on a leap day");
        }

        var (from, to) = PeriodHelper.ComputePriorPeriodRange("this-month", leapDay);
        from.Should().Be(new DateOnly(2028, 1, 1));
        to.Should().Be(new DateOnly(2028, 1, 31), "the whole of the previous month");
    }

    [Fact]
    public void MonthLengthDoesNotDistortTheBaseline()
    {
        // The old slice rule needed clamping because months differ in length. A whole-period baseline
        // simply is the previous month, whatever its length.
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("this-month", new DateOnly(2026, 3, 31));

        from.Should().Be(new DateOnly(2026, 2, 1));
        to.Should().Be(new DateOnly(2026, 2, 28));
    }
}
