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

    /// <summary>
    /// When true, commission is split at the quota boundary: the revenue portion up to quota
    /// earns the rate of the tier that contains it, and the excess above quota earns the rate
    /// of the next tier. When false (default), the classic bracket-lookup applies: the entire
    /// transaction amount earns the single rate of whichever tier the overall attainment falls in.
    /// </summary>
    public bool SplitAtQuota { get; init; }

    public static RateTable AttainmentBased(IReadOnlyList<AttainmentTier> tiers, bool splitAtQuota = false)
    {
        if (tiers.Count == 0)
        {
            throw new DomainException("Attainment-based rate table must have at least one tier.");
        }

        return new() { Type = RateTableType.AttainmentBased, AttainmentTiers = tiers, SplitAtQuota = splitAtQuota };
    }
}
