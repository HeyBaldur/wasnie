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
            bound: RateTableBound.Amount);

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
    /// <param name="bound">
    /// What the bounds are denominated in, as a <see cref="RateTableBound"/> code. The confusion this
    /// validation exists to catch is precisely a unit mix-up — ratios typed into an amount ladder and
    /// amounts typed into a ratio ladder — so a message that does not name the unit is a message that
    /// does not help. It travels as a code, not as a word, because the sentence around it is written
    /// per language.
    /// </param>
    ///
    /// <remarks>
    /// ★★ THE ORDER OF THESE CHECKS IS A DECISION ABOUT WHICH SENTENCE HELPS, NOT AN ACCIDENT.
    /// A malformed ladder almost never breaks exactly one rule, and only the first refusal is ever
    /// seen. Three of these pairs mask systematically, so the checks run from the most structural to
    /// the most local:
    ///
    ///  1. EMPTY first — with no tiers, "the last tier" and "the next tier" do not refer to anything.
    ///
    ///  2. ASCENDING ORDER second, and this is the one that was WRONG. It used to run inside the
    ///     pairwise loop, after the open-last-tier check. But a ladder typed in descending order
    ///     almost always has a bounded last tier too — the tier the author thinks of as the top is
    ///     sitting at the bottom of the list — so the reader was told "your last tier must be
    ///     open-ended", which was true and useless: closing that one tier does not fix a ladder that
    ///     is upside down. A descending ladder ALSO always registers as an overlap (From decreases
    ///     while every To exceeds its own From), which is the second masking pair and the reason this
    ///     must precede the overlap check as well.
    ///
    ///  3. EVERY NON-LAST TIER CLOSED third — structurally required, not merely preferred: the
    ///     overlap and gap checks dereference <c>To</c>, so a middle tier with no upper bound has to
    ///     be refused before they can run at all.
    ///
    ///  4. LAST TIER OPEN, then 5/6 the pairwise overlap and gap. Beyond the three cases above no
    ///     further pair masks systematically — a bounded last tier does not imply an overlap or a
    ///     gap, nor the reverse — so their relative order is left as it was.
    /// </remarks>
    private static void ValidateLadder(
        IReadOnlyList<(decimal From, decimal? To)> tiers,
        string bound)
    {
        // 1 — Non-empty.
        if (tiers.Count == 0)
            throw new DomainCodedException(RateTableInvariant.Empty);

        // 2 — Strictly ascending. Reads only From, so it is safe to run before anything has been
        //     established about upper bounds — and it must, because it is the frame that makes
        //     "last", "overlap" and "gap" mean anything.
        for (var i = 0; i < tiers.Count - 1; i++)
        {
            if (tiers[i].From >= tiers[i + 1].From)
                throw new DomainCodedException(RateTableInvariant.TiersOutOfOrder, new Dictionary<string, object?>
                {
                    ["tierNumber"] = i + 1,
                    ["nextTierNumber"] = i + 2,
                    ["startsAt"] = tiers[i].From,
                    ["nextStartsAt"] = tiers[i + 1].From,
                });
        }

        // 3 — Every tier before the last is closed.
        for (var i = 0; i < tiers.Count - 1; i++)
        {
            if (tiers[i].To is null)
                throw new DomainCodedException(RateTableInvariant.NonLastTierMustBeClosed, new Dictionary<string, object?>
                {
                    ["tierNumber"] = i + 1,
                });
        }

        // 4 — The last tier, and only the last, is open. This is the invariant that was missing
        //     everywhere, and the one that silently zeroed overachievers.
        if (tiers[^1].To is not null)
            throw new DomainCodedException(RateTableInvariant.LastTierMustBeOpen, new Dictionary<string, object?>
            {
                ["tierNumber"] = tiers.Count,
                ["endsAt"] = tiers[^1].To,
                ["bound"] = bound,
            });

        // 5 / 6 — Touch exactly: neither overlapping nor leaving a hole.
        for (var i = 0; i < tiers.Count - 1; i++)
        {
            var upperBound = tiers[i].To!.Value;
            var nextLowerBound = tiers[i + 1].From;

            if (upperBound > nextLowerBound)
                throw new DomainCodedException(RateTableInvariant.TiersOverlap, new Dictionary<string, object?>
                {
                    ["tierNumber"] = i + 1,
                    ["nextTierNumber"] = i + 2,
                    ["endsAt"] = upperBound,
                    ["nextStartsAt"] = nextLowerBound,
                });

            if (upperBound < nextLowerBound)
                throw new DomainCodedException(RateTableInvariant.TiersLeaveGap, new Dictionary<string, object?>
                {
                    ["tierNumber"] = i + 1,
                    ["nextTierNumber"] = i + 2,
                    ["endsAt"] = upperBound,
                    ["nextStartsAt"] = nextLowerBound,
                    ["bound"] = bound,
                });
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
            bound: RateTableBound.AttainmentRatio);

        return new() { Type = RateTableType.AttainmentBased, AttainmentTiers = tiers, SplitAtQuota = splitAtQuota };
    }
}
