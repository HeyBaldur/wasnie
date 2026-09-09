using FluentAssertions;
using Wasnie.Application.Common.Helpers;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-62 — range semantics for the dashboard. Pins the two rules that are easy to get subtly wrong:
/// what "still running" means once a range can end in the future, and what a free range is compared
/// against when there is no named period to step back from.
/// </summary>
public sealed class DashboardRangeHelperTests
{
    private static readonly DateOnly Today = new(2026, 9, 8);

    // ── default range ─────────────────────────────────────────────────────────

    [Fact]
    public void TheDefaultRangeIsTheWholeCurrentMonth_NotTheMonthSoFar()
    {
        var (from, to) = DashboardRangeHelper.DefaultRange(Today);

        from.Should().Be(new DateOnly(2026, 9, 1));
        to.Should().Be(new DateOnly(2026, 9, 30));
    }

    [Theory]
    [InlineData(2026, 2, 28)]   // ordinary February
    [InlineData(2024, 2, 29)]   // leap February
    [InlineData(2026, 12, 31)]
    public void TheDefaultRangeEndsOnTheRealLastDayOfTheMonth(int year, int month, int lastDay)
    {
        var (_, to) = DashboardRangeHelper.DefaultRange(new DateOnly(year, month, 15));

        to.Should().Be(new DateOnly(year, month, lastDay));
    }

    // ── running vs closed ─────────────────────────────────────────────────────

    [Fact]
    public void TheDefaultRangeCountsAsRunning_EvenThoughItEndsInTheFuture()
    {
        // ★ THE REGRESSION THIS TEST EXISTS FOR. The old rule was `to == today`, which was true for the
        // presets because a running period always ended exactly today. The default range ends on the
        // last day of the month, so under that rule it would be classified CLOSED — and the trend band
        // would render eight days of September against the whole of August as a percentage change: the
        // red collapse arrow every first of the month that the pacing design exists to prevent.
        var (_, to) = DashboardRangeHelper.DefaultRange(Today);

        DashboardRangeHelper.IsRunningRange(to, Today).Should().BeTrue();
    }

    [Fact]
    public void ARangeEndingTodayIsStillRunning()
    {
        DashboardRangeHelper.IsRunningRange(Today, Today).Should().BeTrue();
    }

    [Fact]
    public void ARangeThatEndedYesterdayIsClosed()
    {
        DashboardRangeHelper.IsRunningRange(Today.AddDays(-1), Today).Should().BeFalse();
    }

    // ── the comparison window ─────────────────────────────────────────────────

    [Fact]
    public void AWholeCalendarMonthComparesAgainstTheWholePreviousCalendarMonth()
    {
        // Length-matching September (30 days) would give 2–31 August, and the dashboard's August figure
        // would then disagree with the August every other screen reports.
        var (from, to) = DashboardRangeHelper.PriorRange(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        from.Should().Be(new DateOnly(2026, 8, 1));
        to.Should().Be(new DateOnly(2026, 8, 31));
    }

    [Fact]
    public void AWholeMonthAfterAShorterOneStillGetsTheWholePreviousMonth()
    {
        var (from, to) = DashboardRangeHelper.PriorRange(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        from.Should().Be(new DateOnly(2026, 2, 1));
        to.Should().Be(new DateOnly(2026, 2, 28), "February is shorter, and that is the honest comparison");
    }

    [Fact]
    public void AnArbitraryRangeComparesAgainstTheSameNumberOfDaysImmediatelyBefore()
    {
        // 1 Feb – 15 Apr 2026 is 74 days; the window before it is the 74 days ending 31 Jan.
        var from = new DateOnly(2026, 2, 1);
        var to = new DateOnly(2026, 4, 15);

        var (priorFrom, priorTo) = DashboardRangeHelper.PriorRange(from, to);

        priorTo.Should().Be(new DateOnly(2026, 1, 31), "it ends the day before the range starts");
        (priorTo.DayNumber - priorFrom.DayNumber).Should().Be(to.DayNumber - from.DayNumber,
            "the two windows must be the same length or the comparison is not a comparison");
    }

    [Fact]
    public void ASingleDayComparesAgainstThePreviousDay()
    {
        var day = new DateOnly(2026, 5, 20);

        var (from, to) = DashboardRangeHelper.PriorRange(day, day);

        from.Should().Be(new DateOnly(2026, 5, 19));
        to.Should().Be(new DateOnly(2026, 5, 19));
    }

    [Fact]
    public void APartialMonthIsNotTreatedAsAWholeOne()
    {
        // 1–4 August is not August, so it compares against the four days before it, not against July.
        var (from, to) = DashboardRangeHelper.PriorRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 4));

        from.Should().Be(new DateOnly(2026, 7, 28));
        to.Should().Be(new DateOnly(2026, 7, 31));
    }

    [Fact]
    public void ARangeSpanningTwoWholeMonthsIsLengthMatched_NotSteppedByCalendar()
    {
        // Aug 1 – Sep 30 is 61 days. It is not ONE calendar month, so the calendar rule must not fire.
        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 9, 30);

        var (priorFrom, priorTo) = DashboardRangeHelper.PriorRange(from, to);

        priorTo.Should().Be(new DateOnly(2026, 7, 31));
        (priorTo.DayNumber - priorFrom.DayNumber + 1).Should().Be(61);
    }

    [Fact]
    public void TheComparisonWindowNeverOverlapsTheRangeItself()
    {
        foreach (var (from, to) in new[]
                 {
                     (new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
                     (new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 15)),
                     (new DateOnly(2026, 5, 20), new DateOnly(2026, 5, 20)),
                     (new DateOnly(2024, 2, 1), new DateOnly(2024, 2, 29)),
                 })
        {
            var (_, priorTo) = DashboardRangeHelper.PriorRange(from, to);
            priorTo.Should().BeBefore(from, "double-counting a day would inflate both sides of the trend");
        }
    }
}
