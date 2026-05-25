using FluentAssertions;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.UnitTests.Domain;

public sealed class PercentageTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.05)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Create_ValidValue_ReturnsSuccess(double value)
    {
        var result = Percentage.Create((decimal)value);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Value.Should().Be((decimal)value);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(-1.0)]
    [InlineData(2.0)]
    public void Create_OutOfRange_ReturnsFailure(double value)
    {
        var result = Percentage.Create((decimal)value);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("0 and 1");
    }

    [Fact]
    public void FromPercent_ConvertsProperly()
    {
        var pct = Percentage.FromPercent(5m);
        pct.Value.Should().Be(0.05m);
    }

    [Fact]
    public void ToPercent_ConvertsProperly()
    {
        var pct = Percentage.Create(0.05m).Value!;
        pct.ToPercent().Should().Be(5m);
    }

    [Fact]
    public void ToString_FormatsAsPercent()
    {
        var pct = Percentage.Create(0.05m).Value!;
        pct.ToString().Should().Be("5.00%");
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var a = Percentage.Create(0.1m).Value!;
        var b = Percentage.Create(0.1m).Value!;
        a.Should().Be(b);
    }
}
