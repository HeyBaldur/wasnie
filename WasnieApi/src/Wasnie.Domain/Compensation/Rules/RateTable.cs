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
        ValidateLadder(
            tiers.Select(t => (t.From, t.To)).ToList(),
            kind: "Tiered",
            boundName: "amount");

        return new() { Type = RateTableType.Tiered, Tiers = tiers };
    }

    /// <summary>
    /// ★★ THE ONE PLACE A LADDER OF TIERS IS CHECKED, FOR BOTH TABLE TYPES.
    ///
    /// The two factories used to disagree: Tiered checked ordering and overlap, attainment checked
    /// only that the list was not empty. Nothing about that asymmetry was intentional, and the side
    /// that checked nothing is the side whose tables the engine looks up by bracket — so a table
    /// whose top tier stopped short paid ZERO to anyone above it, silently.
    ///
    /// ★ VALIDATION IS WRITE-ONLY, AND THAT IS THE WHOLE CONTRACT OF THIS METHOD. It runs in the
    /// factories, which nothing on the read path calls: EF's value converter
    /// (PlanRuleConfiguration.cs:52) and RuleSnapshotJsonConverter.cs:29 both go through
    /// System.Text.Json, which builds the object property by property. Tables already in the
    /// database that break these rules — and several do — keep loading exactly as before. Refusing
    /// to read one back would not fix the payout it produced; it would hide it.
    ///
    /// ★ CONTIGUITY IS ONE EQUALITY, NOT TWO INEQUALITIES. The engine selects with
    /// <c>From &lt;= x &amp;&amp; x &lt;= To</c> and LastOrDefault (CommissionCalculator.cs:217-220), so bounds
    /// are inclusive at BOTH ends and a shared edge belongs to the upper tier. The reference table
    /// in production — [{0, 1, 4%}, {1, null, 7%}] — therefore has tiers that TOUCH at 1. A
    /// no-overlap rule written the obvious way (<c>To &gt;= next.From</c>) would reject the only
    /// correctly-shaped table anybody has. Touching is required, and its two failure directions get
    /// separate messages because "your tiers overlap" and "your tiers leave a hole" are different
    /// mistakes to a reader.
    /// </summary>
    /// <param name="boundName">
    /// What the bounds are denominated in, for the error text. The confusion this validation exists
    /// to catch is precisely a unit mix-up — ratios typed into an amount ladder and amounts typed
    /// into a ratio ladder — so an error that does not name the unit is an error that does not help.
    /// </param>
    private static void ValidateLadder(
        IReadOnlyList<(decimal From, decimal? To)> tiers,
        string kind,
        string boundName)
    {
        // 1 — Non-empty.
        if (tiers.Count == 0)
            throw new DomainException($"{kind} rate table must have at least one tier.");

        // 2 — The last tier, and only the last, is open. This is the invariant that was missing
        //     everywhere, and the one that silently zeroed overachievers.
        if (tiers[^1].To is not null)
            throw new DomainException(
                $"{kind} rate table: the last tier must be open-ended (no upper bound), so that a " +
                $"{boundName} above every tier still earns a rate. Tier {tiers.Count} ends at " +
                $"{tiers[^1].To}.");

        for (var i = 0; i < tiers.Count - 1; i++)
        {
            // 3 — Every tier before the last is closed.
            if (tiers[i].To is null)
                throw new DomainException(
                    $"{kind} rate table: tier {i + 1} must have an upper bound because it is not the " +
                    "last tier. Only the last tier may be open-ended.");

            // 4 — Strictly ascending.
            if (tiers[i].From >= tiers[i + 1].From)
                throw new DomainException(
                    $"{kind} rate table: tiers must be ordered ascending. Tier {i + 1} starts at " +
                    $"{tiers[i].From} and tier {i + 2} starts at {tiers[i + 1].From}.");

            // 5 / 6 — Touch exactly: neither overlapping nor leaving a hole.
            var upperBound = tiers[i].To!.Value;
            var nextLowerBound = tiers[i + 1].From;

            if (upperBound > nextLowerBound)
                throw new DomainException(
                    $"{kind} rate table: tiers {i + 1} and {i + 2} overlap. Tier {i + 1} ends at " +
                    $"{upperBound} but tier {i + 2} starts at {nextLowerBound}.");

            if (upperBound < nextLowerBound)
                throw new DomainException(
                    $"{kind} rate table: tiers {i + 1} and {i + 2} leave a gap. Tier {i + 1} ends at " +
                    $"{upperBound} and tier {i + 2} starts at {nextLowerBound}; a {boundName} in " +
                    "between would earn no rate at all.");
        }
    }

    /// <summary>
    /// When true, commission is split at the quota boundary: the revenue portion up to quota
    /// earns the rate of the tier that contains it, and the excess above quota earns the rate
    /// of the next tier. When false (default), the classic bracket-lookup applies: the entire
    /// transaction amount earns the single rate of whichever tier the overall attainment falls in.
    /// </summary>
    public bool SplitAtQuota { get; init; }

    /// <param name="tiers">
    /// ★ THE BOUNDS ARE RATIOS OF QUOTA, NOT MONEY: 1 means 100% of target, 1.4 means 140%. Every
    /// attainment table that got this wrong in production had its bounds typed as currency
    /// (0–20000, 20000–50000), which makes every reachable ratio land in the first tier and turns
    /// an accelerator into a flat rate nobody notices.
    /// </param>
    public static RateTable AttainmentBased(IReadOnlyList<AttainmentTier> tiers, bool splitAtQuota = false)
    {
        ValidateLadder(
            tiers.Select(t => (t.AttainmentFrom, t.AttainmentTo)).ToList(),
            kind: "Attainment-based",
            boundName: "attainment ratio");

        return new() { Type = RateTableType.AttainmentBased, AttainmentTiers = tiers, SplitAtQuota = splitAtQuota };
    }
}
