using FluentAssertions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.UnitTests.Domain;

public sealed class DateRangeTests
{
    private static readonly DateOnly Jan1 = new(2025, 1, 1);
    private static readonly DateOnly Dec31 = new(2025, 12, 31);
    private static readonly DateOnly Jul1 = new(2025, 7, 1);

    [Fact]
    public void Of_ValidRange_CreatesInstance()
    {
        var range = DateRange.Of(Jan1, Dec31);
        range.Start.Should().Be(Jan1);
        range.End.Should().Be(Dec31);
    }

    [Fact]
    public void Of_SameStartAndEnd_CreatesInstance()
    {
        var range = DateRange.Of(Jan1, Jan1);
        range.Start.Should().Be(Jan1);
        range.End.Should().Be(Jan1);
    }

    [Fact]
    public void Of_EndBeforeStart_ThrowsDomainException()
    {
        var act = () => DateRange.Of(Dec31, Jan1);
        act.Should().Throw<DomainException>().WithMessage("*end must be on or after start*");
    }

    [Fact]
    public void Contains_DateOnStart_ReturnsTrue()
    {
        var range = DateRange.Of(Jan1, Dec31);
        range.Contains(Jan1).Should().BeTrue();
    }

    [Fact]
    public void Contains_DateOnEnd_ReturnsTrue()
    {
        var range = DateRange.Of(Jan1, Dec31);
        range.Contains(Dec31).Should().BeTrue();
    }

    [Fact]
    public void Contains_DateInMiddle_ReturnsTrue()
    {
        var range = DateRange.Of(Jan1, Dec31);
        range.Contains(Jul1).Should().BeTrue();
    }

    [Fact]
    public void Contains_DateBeforeStart_ReturnsFalse()
    {
        var range = DateRange.Of(Jan1, Dec31);
        range.Contains(Jan1.AddDays(-1)).Should().BeFalse();
    }

    [Fact]
    public void Contains_DateAfterEnd_ReturnsFalse()
    {
        var range = DateRange.Of(Jan1, Dec31);
        range.Contains(Dec31.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void Overlaps_OverlappingRanges_ReturnsTrue()
    {
        var a = DateRange.Of(Jan1, Jul1);
        var b = DateRange.Of(new DateOnly(2025, 6, 1), Dec31);
        a.Overlaps(b).Should().BeTrue();
    }

    [Fact]
    public void Overlaps_AdjacentRanges_ReturnsTrue()
    {
        var a = DateRange.Of(Jan1, Jul1);
        var b = DateRange.Of(Jul1, Dec31);
        a.Overlaps(b).Should().BeTrue(); // Jul1 is shared
    }

    [Fact]
    public void Overlaps_NonOverlappingRanges_ReturnsFalse()
    {
        var a = DateRange.Of(Jan1, new DateOnly(2025, 3, 31));
        var b = DateRange.Of(new DateOnly(2025, 4, 1), Dec31);
        a.Overlaps(b).Should().BeFalse();
    }

    [Fact]
    public void Overlaps_ContainedRange_ReturnsTrue()
    {
        var outer = DateRange.Of(Jan1, Dec31);
        var inner = DateRange.Of(new DateOnly(2025, 3, 1), new DateOnly(2025, 9, 30));
        outer.Overlaps(inner).Should().BeTrue();
    }

    [Fact]
    public void Equality_SameStartAndEnd_AreEqual()
    {
        var a = DateRange.Of(Jan1, Dec31);
        var b = DateRange.Of(Jan1, Dec31);
        a.Should().Be(b);
    }

    [Fact]
    public void ToString_ReturnsIsoFormat()
    {
        var range = DateRange.Of(Jan1, Dec31);
        range.ToString().Should().Be("2025-01-01..2025-12-31");
    }
}
