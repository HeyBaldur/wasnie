using FluentAssertions;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.UnitTests.Domain;

public sealed class AttainmentPercentageTests
{
    [Fact]
    public void Zero_HasValueZero()
    {
        AttainmentPercentage.Zero.Value.Should().Be(0m);
    }

    [Theory]
    [InlineData(50_000, 50_000, 1.0000)]  // exactly 100%
    [InlineData(60_000, 50_000, 1.2000)]  // 120% overachievement
    [InlineData(0, 50_000, 0.0000)]       // 0%
    public void FromAchievedAndTarget_ComputesRatio(double achievedD, double targetD, double expectedD)
    {
        var result = AttainmentPercentage.FromAchievedAndTarget((decimal)achievedD, (decimal)targetD);
        result.Value.Should().Be((decimal)expectedD);
    }

    [Fact]
    public void FromAchievedAndTarget_PartialAttainment_RoundsCorrectly()
    {
        // 37,714.32 / 50,000 = 0.754286... → banker's rounding to 4 places = 0.7543
        var result = AttainmentPercentage.FromAchievedAndTarget(37_714.32m, 50_000m);
        result.Value.Should().Be(0.7543m);
    }

    [Fact]
    public void FromAchievedAndTarget_TargetZero_ReturnsZero()
    {
        var result = AttainmentPercentage.FromAchievedAndTarget(1000m, 0m);
        result.Should().Be(AttainmentPercentage.Zero);
    }

    [Fact]
    public void FromAchievedAndTarget_NegativeTarget_ReturnsZero()
    {
        var result = AttainmentPercentage.FromAchievedAndTarget(1000m, -500m);
        result.Should().Be(AttainmentPercentage.Zero);
    }

    [Fact]
    public void FromAchievedAndTarget_NegativeAchieved_ClampsToZero()
    {
        var result = AttainmentPercentage.FromAchievedAndTarget(-100m, 1000m);
        result.Value.Should().Be(0m);
    }

    [Theory]
    [InlineData(0.76, "76%")]
    [InlineData(1.20, "120%")]
    [InlineData(1.50, "150%")]
    [InlineData(0.0, "0%")]
    [InlineData(1.0, "100%")]
    public void ToPercentString_FormatsCorrectly(double valueDouble, string expected)
    {
        var value = (decimal)valueDouble;
        var ap = AttainmentPercentage.FromAchievedAndTarget(value * 100m, 100m);
        ap.ToPercentString().Should().Be(expected);
    }

    [Fact]
    public void ValueEquality_SameValue_AreEqual()
    {
        var a = AttainmentPercentage.FromAchievedAndTarget(75m, 100m);
        var b = AttainmentPercentage.FromAchievedAndTarget(75m, 100m);
        a.Should().Be(b);
    }

    [Fact]
    public void ValueEquality_DifferentValues_AreNotEqual()
    {
        var a = AttainmentPercentage.FromAchievedAndTarget(75m, 100m);
        var b = AttainmentPercentage.FromAchievedAndTarget(80m, 100m);
        a.Should().NotBe(b);
    }

    [Fact]
    public void Exceeds100Percent_IsAllowed()
    {
        var result = AttainmentPercentage.FromAchievedAndTarget(200m, 100m);
        result.Value.Should().Be(2.0m);
        result.ToPercentString().Should().Be("200%");
    }
}
