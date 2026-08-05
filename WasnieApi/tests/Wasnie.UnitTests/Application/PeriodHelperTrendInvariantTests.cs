using FluentAssertions;
using Wasnie.Application.Common.Helpers;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// THE TREND INVARIANT — one comparison rule for every period, no exceptions:
///
///   · A period still RUNNING is compared against the EQUIVALENT ELAPSED SLICE of the previous one
///     (the same number of days), clamped to that period's end.
///   · A period already CLOSED is compared against the whole of the previous one.
///
/// This existed for quarters but not for "this-month", which compared a handful of elapsed days against
/// a FULL previous month. On 5 August that read five days of August against all thirty-one of July and
/// printed roughly -85% in red — a collapse that never happened — and moving the filter from This Month
/// to This Quarter silently swapped the formula underneath the user.
///
/// These tests exist to make that divergence impossible to reintroduce quietly.
/// </summary>
public sealed class PeriodHelperTrendInvariantTests
{
    // Deliberately the date from the bug report: 5 days into August.
    private static readonly DateOnly EarlyAugust = new(2026, 8, 5);

    private static readonly string[] RunningPeriods = ["this-month", "active", "this-quarter", "ytd"];
    private static readonly string[] ClosedPeriods = ["last-month", "last-quarter", "last-year"];

    // ── the fix ───────────────────────────────────────────────────────────────

    [Fact]
    public void ThisMonth_ComparesAgainstTheEquivalentSlice_NotTheWholePreviousMonth()
    {
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("this-month", EarlyAugust);

        from.Should().Be(new DateOnly(2026, 7, 1));
        to.Should().Be(new DateOnly(2026, 7, 5),
            "five days of August must be compared against five days of July");
        to.Should().NotBe(new DateOnly(2026, 7, 31),
            "comparing against the whole of July is what produced the fictitious -85% collapse");
    }

    [Fact]
    public void ThisMonth_ComparedWindowIsTheSameLengthAsTheCurrentOne()
    {
        var (curFrom, curTo) = PeriodHelper.ComputeDateRange("this-month", EarlyAugust);
        var (priFrom, priTo) = PeriodHelper.ComputePriorPeriodRange("this-month", EarlyAugust);

        var currentDays = curTo!.Value.DayNumber - curFrom!.Value.DayNumber;
        var priorDays = priTo!.Value.DayNumber - priFrom!.Value.DayNumber;

        priorDays.Should().Be(currentDays);
    }

    [Fact]
    public void ActiveAlias_FollowsTheSameRuleAsThisMonth()
    {
        PeriodHelper.ComputePriorPeriodRange("active", EarlyAugust)
            .Should().Be(PeriodHelper.ComputePriorPeriodRange("this-month", EarlyAugust));
    }

    // ── the invariant, stated over every period at once ───────────────────────

    [Fact]
    public void EveryRunningPeriod_ComparesAgainstAnEquallyLongWindow()
    {
        // The consistency test the WI asks for: if any running period ever reverts to comparing against
        // a full previous period, its prior window stops matching its current window and this fails.
        foreach (var period in RunningPeriods)
        {
            var (curFrom, curTo) = PeriodHelper.ComputeDateRange(period, EarlyAugust);
            var (priFrom, priTo) = PeriodHelper.ComputePriorPeriodRange(period, EarlyAugust);

            priFrom.Should().NotBeNull($"{period} must have a comparison window");
            var currentDays = curTo!.Value.DayNumber - curFrom!.Value.DayNumber;
            var priorDays = priTo!.Value.DayNumber - priFrom!.Value.DayNumber;

            priorDays.Should().Be(currentDays,
                $"{period} is still running, so it must be compared against an equally long slice");
        }
    }

    [Fact]
    public void EveryClosedPeriod_ComparesAgainstTheWholePreviousPeriod()
    {
        foreach (var period in ClosedPeriods)
        {
            var (priFrom, priTo) = PeriodHelper.ComputePriorPeriodRange(period, EarlyAugust);
            var (curFrom, curTo) = PeriodHelper.ComputeDateRange(period, EarlyAugust);

            priFrom.Should().NotBeNull($"{period} must have a comparison window");

            // A closed period and the one before it abut exactly, and the comparison window runs to the
            // very end of that previous period — never a partial slice of it.
            priTo!.Value.AddDays(1).Should().Be(curFrom!.Value,
                $"{period} must be compared against the FULL preceding period");
            curTo.Should().NotBe(EarlyAugust, $"{period} is supposed to be a closed period");
        }
    }

    [Fact]
    public void RunningAndClosedPeriodsAreTheOnlyTwoCases_AndTheSplitIsDerivedFromTheRangeItself()
    {
        // "Running" is not a hand-maintained list in the production code — it is derived from whether the
        // period's own range ends today. This pins that classification so a future period key cannot be
        // added on one side while behaving like the other.
        foreach (var period in RunningPeriods)
        {
            var (_, to) = PeriodHelper.ComputeDateRange(period, EarlyAugust);
            to.Should().Be(EarlyAugust, $"{period} is a running period, so its range ends today");
        }

        foreach (var period in ClosedPeriods)
        {
            var (_, to) = PeriodHelper.ComputeDateRange(period, EarlyAugust);
            to.Should().NotBe(EarlyAugust, $"{period} is a closed period, so its range ended in the past");
        }
    }

    // ── month-length clamping (the counterpart of the quarter clamp) ──────────

    [Fact]
    public void ThisMonth_On31March_ClampsToTheEndOfFebruary()
    {
        // 31 days elapsed in March; February 2026 has only 28. Without the clamp the prior window would
        // run to 3 March — inside the current month — and count the same days on both sides.
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("this-month", new DateOnly(2026, 3, 31));

        from.Should().Be(new DateOnly(2026, 2, 1));
        to.Should().Be(new DateOnly(2026, 2, 28), "clamped to the last day of February");
    }

    [Fact]
    public void ThisMonth_On31May_ClampsToTheEndOfApril()
    {
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("this-month", new DateOnly(2026, 5, 31));

        from.Should().Be(new DateOnly(2026, 4, 1));
        to.Should().Be(new DateOnly(2026, 4, 30), "April has 30 days, so the slice cannot reach a 31st");
    }

    [Fact]
    public void ThisMonth_On29FebruaryOfALeapYear_IsHandled()
    {
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("this-month", new DateOnly(2028, 2, 29));

        from.Should().Be(new DateOnly(2028, 1, 1));
        to.Should().Be(new DateOnly(2028, 1, 29), "29 elapsed days of February against 29 of January");
    }

    [Fact]
    public void ThisMonth_OnTheFirstOfTheMonth_ComparesAgainstASingleDay()
    {
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("this-month", new DateOnly(2026, 8, 1));

        from.Should().Be(new DateOnly(2026, 7, 1));
        to.Should().Be(new DateOnly(2026, 7, 1), "zero days elapsed → the first day of the previous month");
    }

    [Fact]
    public void ThisMonth_PriorWindowNeverSpillsIntoTheCurrentMonth()
    {
        // Every day of a 31-day month following a 28-day one — the worst case for the clamp.
        for (var day = 1; day <= 31; day++)
        {
            var today = new DateOnly(2026, 3, day);
            var (_, priorTo) = PeriodHelper.ComputePriorPeriodRange("this-month", today);

            priorTo!.Value.Should().BeOnOrBefore(new DateOnly(2026, 2, 28),
                $"on {today:yyyy-MM-dd} the comparison window must stay inside February");
        }
    }

    // ── closed periods are untouched by this change ───────────────────────────

    [Fact]
    public void LastMonth_StillComparesAgainstTheWholeMonthBefore()
    {
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("last-month", EarlyAugust);

        from.Should().Be(new DateOnly(2026, 6, 1));
        to.Should().Be(new DateOnly(2026, 6, 30));
    }

    [Fact]
    public void LastQuarter_StillComparesAgainstTheWholeQuarterBefore()
    {
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("last-quarter", EarlyAugust);

        from.Should().Be(new DateOnly(2026, 1, 1));
        to.Should().Be(new DateOnly(2026, 3, 31));
    }

    [Fact]
    public void LastYear_StillComparesAgainstTheWholeYearBefore()
    {
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("last-year", EarlyAugust);

        from.Should().Be(new DateOnly(2024, 1, 1));
        to.Should().Be(new DateOnly(2024, 12, 31));
    }

    // ── unbounded periods still opt out of trend entirely ─────────────────────

    [Theory]
    [InlineData("all-time")]
    [InlineData("next-tuesday")]
    public void PeriodsWithoutBounds_HaveNoComparisonWindow(string period)
    {
        var (from, to) = PeriodHelper.ComputePriorPeriodRange(period, EarlyAugust);

        from.Should().BeNull();
        to.Should().BeNull();
    }

    // ── trend labels must name the windows actually compared ─────────────────

    [Fact]
    public void RunningPeriod_TrendLabelsNameTheSlices_NotTheWholePeriod()
    {
        // The contradiction this prevents: labelling a five-day slice "July 2026" next to €0, while the
        // Last Month screen reports July at €4,939.41. Same product, two irreconcilable claims.
        var (current, prior) = PeriodHelper.GetTrendLabels("this-month", EarlyAugust);

        current.Should().Be("1–5 Aug 2026");
        prior.Should().Be("1–5 Jul 2026");
        prior.Should().NotBe("July 2026", "the compared window is five days, not the whole month");
    }

    [Fact]
    public void RunningQuarter_TrendLabelsSpanningTwoMonths_ReadAsARange()
    {
        var (current, prior) = PeriodHelper.GetTrendLabels("this-quarter", EarlyAugust);

        current.Should().Be("1 Jul – 5 Aug 2026");
        prior.Should().Be("1 Apr – 6 May 2026");
    }

    [Fact]
    public void ClosedPeriod_TrendLabelsKeepThePeriodNames()
    {
        // Whole against whole — the period names are exact and read better than two date ranges.
        PeriodHelper.GetTrendLabels("last-month", EarlyAugust).Should().Be(("July 2026", "June 2026"));
        PeriodHelper.GetTrendLabels("last-quarter", EarlyAugust).Should().Be(("Q2 2026", "Q1 2026"));
        PeriodHelper.GetTrendLabels("last-year", EarlyAugust).Should().Be(("2025", "2024"));
    }

    [Fact]
    public void FirstDayOfAPeriod_TrendLabelIsASingleDay()
    {
        var (current, prior) = PeriodHelper.GetTrendLabels("this-month", new DateOnly(2026, 8, 1));

        current.Should().Be("1 Aug 2026");
        prior.Should().Be("1 Jul 2026");
    }

    [Fact]
    public void RunningYtd_PriorLabelCrossesTheYearAndSaysSo()
    {
        var (current, prior) = PeriodHelper.GetTrendLabels("ytd", EarlyAugust);

        current.Should().Be("1 Jan – 5 Aug 2026");
        prior.Should().Be("1 Jan – 5 Aug 2025");
    }
}
