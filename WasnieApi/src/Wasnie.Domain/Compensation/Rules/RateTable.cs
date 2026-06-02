using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Rules;

public sealed class RateTable
{
    public int _schema { get; init; } = 1;
    public RateTableType Type { get; init; }
    public decimal? FlatRate { get; init; }
    public IReadOnlyList<RateTier>? Tiers { get; init; }
    public IReadOnlyList<AttainmentTier>? AttainmentTiers { get; init; }

    public static RateTable Flat(decimal rate) => new() { Type = RateTableType.Flat, FlatRate = rate };

    public static RateTable Tiered(IReadOnlyList<RateTier> tiers)
    {
        if (tiers.Count == 0)
        {
            throw new DomainException("Tiered rate table must have at least one tier.");
        }

        for (var i = 0; i < tiers.Count - 1; i++)
        {
            if (tiers[i].To is null)
            {
                throw new DomainException($"Tier at index {i} must have an upper bound when it is not the last tier.");
            }

            if (tiers[i].To!.Value > tiers[i + 1].From)
            {
                throw new DomainException("Tier ranges must be non-overlapping and ordered ascending.");
            }
        }

        return new() { Type = RateTableType.Tiered, Tiers = tiers };
    }

    public static RateTable AttainmentBased(IReadOnlyList<AttainmentTier> tiers)
    {
        if (tiers.Count == 0)
        {
            throw new DomainException("Attainment-based rate table must have at least one tier.");
        }

        return new() { Type = RateTableType.AttainmentBased, AttainmentTiers = tiers };
    }
}
