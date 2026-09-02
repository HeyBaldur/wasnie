namespace Wasnie.Domain.Compensation.Rules;

/// <summary>
/// The six ways a ladder of tiers can be malformed, as codes.
///
/// ★ THESE STRINGS ARE AN API, NOT A MESSAGE — the same contract <c>PayoutSkipReason</c> carries.
/// The front end matches them against its own EN/ES/PL translations; renaming one silently degrades
/// the rule form to its neutral fallback, which says the rate table was rejected without saying why.
/// Add a code and its three translations in the same change.
///
/// ★ THE ORDER THEY ARE CHECKED IN IS NOT THE ORDER THEY ARE LISTED IN. See
/// <c>RateTable.ValidateLadder</c>: a ladder usually breaks several of these at once, and which one
/// gets reported is a deliberate decision about which sentence helps.
/// </summary>
public static class RateTableInvariant
{
    /// <summary>A table with no tiers at all. Nothing else can be said about it.</summary>
    public const string Empty = "RateTableEmpty";

    /// <summary>
    /// The last tier has an upper bound, so anything above the top bracket earns no rate. ★ THE
    /// INVARIANT THAT SILENTLY PAID ZERO to overachievers, and the reason this validation exists.
    /// </summary>
    public const string LastTierMustBeOpen = "RateTableLastTierMustBeOpen";

    /// <summary>A tier that is not the last has no upper bound. Only the last one may be open.</summary>
    public const string NonLastTierMustBeClosed = "RateTableNonLastTierMustBeClosed";

    /// <summary>Two consecutive tiers do not start in ascending order.</summary>
    public const string TiersOutOfOrder = "RateTableTiersOutOfOrder";

    /// <summary>One tier ends beyond where the next begins.</summary>
    public const string TiersOverlap = "RateTableTiersOverlap";

    /// <summary>One tier ends short of where the next begins, leaving values with no rate.</summary>
    public const string TiersLeaveGap = "RateTableTiersLeaveGap";

    /// <summary>
    /// A rate above <see cref="RateMagnitude.MaxFractionalRate"/> — the commission would exceed the
    /// sale it is paid on. ★ THE SEVENTH INVARIANT, AND THE ONLY ONE ABOUT THE RATE RATHER THAN THE
    /// SHAPE: it is what `4` typed for "4%" looks like, and the engine pays it 400% without a word.
    /// </summary>
    public const string RateAboveMaximum = "RateTableRateAboveMaximum";

    /// <summary>A negative rate, which would take money back on every sale that matched.</summary>
    public const string RateBelowZero = "RateTableRateBelowZero";
}

/// <summary>
/// What a ladder's bounds are denominated in, as a code.
///
/// ★ THE UNIT IS THE POINT OF THE MESSAGE, NOT DECORATION. The mistake this validation was built to
/// catch is precisely a unit mix-up — attainment ratios typed into a money ladder, and money typed
/// into a ratio ladder (every broken attainment table in production had bounds like 0–20000 instead
/// of 0–1). A message that does not name the unit does not help the person who made that mistake.
/// It travels as a code for the same reason the invariants do: "amount" and "attainment ratio"
/// decline differently in Polish, so the two sentences are written out separately per language
/// rather than assembled from a translated noun.
/// </summary>
public static class RateTableBound
{
    /// <summary>Money, in the plan's currency.</summary>
    public const string Amount = "Amount";

    /// <summary>A ratio of quota: 1 is 100% of target, not one unit of currency.</summary>
    public const string AttainmentRatio = "AttainmentRatio";
}
