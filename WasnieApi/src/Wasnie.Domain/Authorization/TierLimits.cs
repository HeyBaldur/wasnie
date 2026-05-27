namespace Wasnie.Domain.Authorization;

public sealed record TierLimitValues(int MaxPayees, int MaxPlans);

public static class TierLimits
{
    public static readonly IReadOnlyDictionary<Tier, TierLimitValues> Limits =
        new Dictionary<Tier, TierLimitValues>
        {
            [Tier.Free]       = new(MaxPayees: 5,           MaxPlans: 1),
            [Tier.Starter]    = new(MaxPayees: 25,          MaxPlans: 5),
            [Tier.Growth]     = new(MaxPayees: 75,          MaxPlans: 15),
            [Tier.Scale]      = new(MaxPayees: 150,         MaxPlans: int.MaxValue),
            [Tier.Enterprise] = new(MaxPayees: int.MaxValue, MaxPlans: int.MaxValue),
        };
}
