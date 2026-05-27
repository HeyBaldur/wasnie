using FluentAssertions;
using Wasnie.Domain.Authorization;

namespace Wasnie.UnitTests.Authorization;

public sealed class TierLimitsTests
{
    [Theory]
    [InlineData(Tier.Free, 5, 1)]
    [InlineData(Tier.Starter, 25, 5)]
    [InlineData(Tier.Growth, 75, 15)]
    [InlineData(Tier.Scale, 150, int.MaxValue)]
    [InlineData(Tier.Enterprise, int.MaxValue, int.MaxValue)]
    public void Limits_HaveCorrectValues(Tier tier, int expectedPayees, int expectedPlans)
    {
        var limits = TierLimits.Limits[tier];
        limits.MaxPayees.Should().Be(expectedPayees);
        limits.MaxPlans.Should().Be(expectedPlans);
    }

    [Fact]
    public void AllTiers_HaveEntries()
    {
        foreach (Tier tier in Enum.GetValues<Tier>())
        {
            TierLimits.Limits.Should().ContainKey(tier);
        }
    }

    [Fact]
    public void PayeeLimits_AreStrictlyIncreasing()
    {
        var limits = TierLimits.Limits;
        limits[Tier.Free].MaxPayees.Should().BeLessThan(limits[Tier.Starter].MaxPayees);
        limits[Tier.Starter].MaxPayees.Should().BeLessThan(limits[Tier.Growth].MaxPayees);
        limits[Tier.Growth].MaxPayees.Should().BeLessThan(limits[Tier.Scale].MaxPayees);
    }
}
