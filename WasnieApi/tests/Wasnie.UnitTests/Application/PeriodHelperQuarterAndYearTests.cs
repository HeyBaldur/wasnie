using FluentAssertions;
using Wasnie.Application.Common.Helpers;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The quarter and year selectors added for the dashboard quick-filters.
///
/// Quarters are CALENDAR quarters — Q1 Jan–Mar, Q2 Apr–Jun, Q3 Jul–Sep, Q4 Oct–Dec. Wasnie has no
/// fiscal-year concept anywhere in the domain, so there is no offset to honour.
///
/// Date arithmetic is easy to get subtly wrong at the edges, and every one of these ranges scopes the
/// whole dashboard — including the money widgets — so the boundaries are pinned explicitly rather than
/// derived in the assertions.
/// </summary>
public sealed class PeriodHelperQuarterAndYearTests
{
    // ── this-quarter (QTD) ────────────────────────────────────────────────────

    [Theory]
    // (today) → (expected quarter start)
    [InlineData(2026, 1, 1, 1)]    // first day of Q1
    [InlineData(2026, 2, 14, 1)]
    [InlineData(2026, 3, 31, 1)]   // last day of Q1
    [InlineData(2026, 4, 1, 4)]    // first day of Q2
    [InlineData(2026, 6, 30, 4)]
    [InlineData(2026, 7, 1, 7)]    // first day of Q3
    [InlineData(2026, 9, 30, 7)]
    [InlineData(2026, 10, 1, 10)]  // first day of Q4
    [InlineData(2026, 12, 31, 10)]
    public void ThisQuarter_RunsFromTheQuarterStartToToday(
        int year, int month, int day, int expectedStartMonth)
    {
        var today = new DateOnly(year, month, day);

        var (from, to) = PeriodHelper.ComputeDateRange("this-quarter", today);

        from.Should().Be(new DateOnly(year, expectedStartMonth, 1));
        to.Should().Be(today, "the quarter is still running, so it ends today — it is quarter-TO-DATE");
    }

    // ── last-quarter (closed) ─────────────────────────────────────────────────

    [Theory]
    [InlineData(2026, 8, 15, 2026, 4, 1, 2026, 6, 30)]   // in Q3 → Q2
    [InlineData(2026, 5, 2, 2026, 1, 1, 2026, 3, 31)]    // in Q2 → Q1
    [InlineData(2026, 11, 20, 2026, 7, 1, 2026, 9, 30)]  // in Q4 → Q3
    public void LastQuarter_IsThePreviousQuarterInFull(
        int y, int m, int d,
        int fromY, int fromM, int fromD,
        int toY, int toM, int toD)
    {
        var (from, to) = PeriodHelper.ComputeDateRange("last-quarter", new DateOnly(y, m, d));

        from.Should().Be(new DateOnly(fromY, fromM, fromD));
        to.Should().Be(new DateOnly(toY, toM, toD));
    }

    [Fact]
    public void LastQuarter_FromQ1_CrossesIntoThePreviousYear()
    {
        // The rollover that off-by-one errors live in: standing in Q1 2026, "last quarter" is Q4 2025.
        var (from, to) = PeriodHelper.ComputeDateRange("last-quarter", new DateOnly(2026, 2, 10));

        from.Should().Be(new DateOnly(2025, 10, 1));
        to.Should().Be(new DateOnly(2025, 12, 31));
    }

    [Fact]
    public void LastQuarter_EndsTheDayBeforeTheCurrentQuarterStarts_NoGapNoOverlap()
    {
        var today = new DateOnly(2026, 8, 15);

        var (_, lastTo) = PeriodHelper.ComputeDateRange("last-quarter", today);
        var (thisFrom, _) = PeriodHelper.ComputeDateRange("this-quarter", today);

        lastTo!.Value.AddDays(1).Should().Be(thisFrom!.Value,
            "consecutive quarters must abut exactly — a gap loses a day of data, an overlap counts one twice");
    }

    // ── ytd / last-year ───────────────────────────────────────────────────────

    [Fact]
    public void Ytd_RunsFromJanuaryFirstToToday()
    {
        var (from, to) = PeriodHelper.ComputeDateRange("ytd", new DateOnly(2026, 8, 5));

        from.Should().Be(new DateOnly(2026, 1, 1));
        to.Should().Be(new DateOnly(2026, 8, 5));
    }

    [Fact]
    public void LastYear_IsThePreviousCalendarYearInFull()
    {
        var (from, to) = PeriodHelper.ComputeDateRange("last-year", new DateOnly(2026, 8, 5));

        from.Should().Be(new DateOnly(2025, 1, 1));
        to.Should().Be(new DateOnly(2025, 12, 31),
            "last year is a CLOSED period — it ends on 31 December, not on today's date last year");
    }

    [Fact]
    public void LastYear_IsUnaffectedByHowFarIntoTheYearWeAre()
    {
        var early = PeriodHelper.ComputeDateRange("last-year", new DateOnly(2026, 1, 2));
        var late = PeriodHelper.ComputeDateRange("last-year", new DateOnly(2026, 12, 31));

        early.Should().Be(late);
    }

    // ── prior-period comparisons ──────────────────────────────────────────────

    [Fact]
    public void ThisQuarter_ComparesAgainstTheSameElapsedSliceOfThePreviousQuarter()
    {
        // 15 days into Q3 (1–15 July). The comparison must be 1–15 April, not the whole of Q2 —
        // otherwise every quarter opens by reporting a collapse against a full previous quarter.
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("this-quarter", new DateOnly(2026, 7, 15));

        from.Should().Be(new DateOnly(2026, 4, 1));
        to.Should().Be(new DateOnly(2026, 4, 15));
    }

    [Fact]
    public void ThisQuarter_PriorSlice_IsClampedToTheShorterPreviousQuarter()
    {
        // Q2 2026 (Apr–Jun) is 91 days; Q1 2026 is 90. On the 91st day of Q2 the naive offset would
        // land on 1 April — inside Q2 itself — and the "previous quarter" total would include days
        // from the current one.
        var lastDayOfQ2 = new DateOnly(2026, 6, 30);

        var (from, to) = PeriodHelper.ComputePriorPeriodRange("this-quarter", lastDayOfQ2);

        from.Should().Be(new DateOnly(2026, 1, 1));
        to.Should().Be(new DateOnly(2026, 3, 31), "clamped to the end of Q1, never spilling into Q2");
    }

    [Fact]
    public void ThisQuarter_OnTheFirstDay_ComparesAgainstASingleDay()
    {
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("this-quarter", new DateOnly(2026, 7, 1));

        from.Should().Be(new DateOnly(2026, 4, 1));
        to.Should().Be(new DateOnly(2026, 4, 1), "zero days elapsed → the first day of the prior quarter");
    }

    [Fact]
    public void LastQuarter_ComparesAgainstTheQuarterBeforeIt_InFull()
    {
        // Standing in Q3 2026: the selector shows Q2, so the comparison is Q1.
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("last-quarter", new DateOnly(2026, 8, 15));

        from.Should().Be(new DateOnly(2026, 1, 1));
        to.Should().Be(new DateOnly(2026, 3, 31));
    }

    [Fact]
    public void LastYear_ComparesAgainstTheYearBeforeIt_InFull()
    {
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("last-year", new DateOnly(2026, 8, 5));

        from.Should().Be(new DateOnly(2024, 1, 1));
        to.Should().Be(new DateOnly(2024, 12, 31));
    }

    [Fact]
    public void Ytd_OnALeapDay_DoesNotThrow()
    {
        // 29 February has no counterpart in a common year, and reconstructing that calendar date threw,
        // so the dashboard failed to load for anyone opening it on a leap day. Elapsed-day arithmetic
        // cannot build an invalid date, so the crash is now structurally impossible.
        var leapDay = new DateOnly(2028, 2, 29);

        var act = () => PeriodHelper.ComputePriorPeriodRange("ytd", leapDay);

        act.Should().NotThrow();
        var (from, to) = PeriodHelper.ComputePriorPeriodRange("ytd", leapDay);
        from.Should().Be(new DateOnly(2027, 1, 1));
        // 60 days of 2028 against 60 days of 2027. Under the universal elapsed-days rule the counterpart
        // of 29 February is 1 March, not 28 February: a leap year genuinely has one more day by that
        // point, and pacing compares equal amounts of time, not equal calendar labels.
        to.Should().Be(new DateOnly(2027, 3, 1), "the same number of elapsed days, not the same date");
    }

    // ── labels ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2026, 2, 10, "Q1 2026")]
    [InlineData(2026, 5, 10, "Q2 2026")]
    [InlineData(2026, 8, 10, "Q3 2026")]
    [InlineData(2026, 11, 10, "Q4 2026")]
    public void ThisQuarter_LabelNamesTheQuarter(int y, int m, int d, string expected) =>
        PeriodHelper.GetPeriodLabel("this-quarter", new DateOnly(y, m, d)).Should().Be(expected);

    [Fact]
    public void LastQuarter_LabelCrossesTheYearBoundaryCorrectly() =>
        PeriodHelper.GetPeriodLabel("last-quarter", new DateOnly(2026, 2, 10))
            .Should().Be("Q4 2025");

    [Fact]
    public void LastYear_LabelIsTheYearItself() =>
        PeriodHelper.GetPeriodLabel("last-year", new DateOnly(2026, 8, 5)).Should().Be("2025");

    [Fact]
    public void PriorLabels_NameThePeriodBeingComparedAgainst()
    {
        var today = new DateOnly(2026, 8, 5);

        PeriodHelper.GetPriorPeriodLabel("this-quarter", today).Should().Be("Q2 2026");
        PeriodHelper.GetPriorPeriodLabel("last-quarter", today).Should().Be("Q1 2026");
        PeriodHelper.GetPriorPeriodLabel("last-year", today).Should().Be("2024");
    }

    // ── all-time is no longer offered on the dashboard, but must still resolve ─

    [Fact]
    public void AllTime_StillResolvesToAnUnfilteredRange()
    {
        // The dashboard dropped the option, but the payouts list, the pay-runs list and the payee detail
        // screen still send this key. Removing the mapping would silently turn their filter into
        // "this-month" via the null-coalescing default and quietly hide rows.
        var (from, to) = PeriodHelper.ComputeDateRange("all-time", new DateOnly(2026, 8, 5));

        from.Should().BeNull();
        to.Should().BeNull();
    }

    [Fact]
    public void UnknownPeriod_DegradesToUnfiltered_RatherThanThrowing()
    {
        var (from, to) = PeriodHelper.ComputeDateRange("next-tuesday", new DateOnly(2026, 8, 5));

        from.Should().BeNull();
        to.Should().BeNull();
    }
}
