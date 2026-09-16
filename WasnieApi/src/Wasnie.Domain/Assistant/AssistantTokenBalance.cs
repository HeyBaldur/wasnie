namespace Wasnie.Domain.Assistant;

/// <summary>One purchased batch, reduced to what the balance walk needs.</summary>
/// <param name="Tokens">Tokens the lot granted.</param>
/// <param name="PurchasedAt">Orders the walk — oldest first.</param>
/// <param name="ExpiresAt">When what is left of the lot dies.</param>
public readonly record struct BoostLot(long Tokens, DateTimeOffset PurchasedAt, DateTimeOffset ExpiresAt);

/// <summary>
/// What a tenant has left of its assistant tokens (KAN-83), derived — never stored.
/// </summary>
/// <param name="IncludedLimit">The plan's monthly allowance for this period.</param>
/// <param name="IncludedUsed">Spent in the CURRENT period, allowance and boost together.</param>
/// <param name="IncludedRemaining">What is left of the allowance. Zero once the period went past it.</param>
/// <param name="BoostRemaining">Live purchased tokens left, expiry and past periods already taken out.</param>
/// <param name="BoostExpired">Purchased tokens that died unused — shown so the loss is visible, not silent.</param>
/// <param name="BoostNextExpiry">When the oldest surviving lot dies. Null when no boost remains.</param>
public sealed record AssistantTokenBalance(
    long IncludedLimit,
    long IncludedUsed,
    long IncludedRemaining,
    long BoostRemaining,
    long BoostExpired,
    DateTimeOffset? BoostNextExpiry)
{
    /// <summary>Everything the tenant may still spend right now.</summary>
    public long TotalRemaining => IncludedRemaining + BoostRemaining;

    /// <summary>True when the assistant must stop for this tenant.</summary>
    public bool Exhausted => TotalRemaining <= 0;
}

/// <summary>
/// The one rule that turns purchases, past periods and this period's usage into a balance (KAN-83).
///
/// ★ PURE, AND THAT IS THE POINT. The refusal that stops the assistant, the meter on the settings screen and the
/// checkout's "you have N left" must never disagree — a screen saying 40% used beside an assistant saying "out of
/// tokens" is the bug report this class exists to prevent. One function, no I/O, so all three read the same answer and
/// the money tests can drive it directly.
///
/// ★ INCLUDED FIRST, THEN BOOST. The allowance resets in a few days and is worth nothing kept; the boost was paid for
/// and expires in a year. Spending the perishable one first is what preserves the customer's money — the reverse would
/// quietly burn what they bought while free tokens went unused.
///
/// ★ OLDEST LOT FIRST. Consumption is charged to the lot closest to expiring, so a customer loses tokens to expiry only
/// when they genuinely could not spend them in time.
/// </summary>
public static class AssistantBoostLedger
{
    /// <param name="includedLimit">The plan's allowance for the current period. Zero for an account with none.</param>
    /// <param name="usedInPeriod">Tokens spent since the period began (the usage meter's sum).</param>
    /// <param name="lots">Every boost the tenant ever bought, in any order.</param>
    /// <param name="boostAlreadyCharged">The sum of the closed periods' overage (<see cref="AssistantBoostDebit"/>).</param>
    /// <param name="now">Decides which lots have died.</param>
    public static AssistantTokenBalance Compute(
        long includedLimit,
        long usedInPeriod,
        IEnumerable<BoostLot> lots,
        long boostAlreadyCharged,
        DateTimeOffset now)
    {
        var includedRemaining = Math.Max(0, includedLimit - usedInPeriod);

        // What this period has already pushed past the allowance lands on the boost, on top of every closed period's.
        var overageThisPeriod = Math.Max(0, usedInPeriod - includedLimit);
        var toCharge = Math.Max(0, boostAlreadyCharged) + overageThisPeriod;

        long remaining = 0;
        long expired = 0;
        DateTimeOffset? nextExpiry = null;

        foreach (var lot in lots.OrderBy(l => l.PurchasedAt).ThenBy(l => l.ExpiresAt))
        {
            var left = lot.Tokens;

            // ★ CONSUMPTION IS CHARGED BEFORE EXPIRY IS APPLIED, oldest lot first. The usage rows say how much boost was
            // spent but not which lot paid for it, so the walk assigns it the way the customer would want: to the tokens
            // that were about to die anyway. Charging a live lot while an expiring one sat unused would manufacture an
            // expiry loss that never had to happen.
            var taken = Math.Min(left, toCharge);
            left -= taken;
            toCharge -= taken;

            if (left <= 0)
                continue;

            if (lot.ExpiresAt <= now)
            {
                expired += left;
                continue;
            }

            remaining += left;
            if (nextExpiry is null || lot.ExpiresAt < nextExpiry)
                nextExpiry = lot.ExpiresAt;
        }

        // ★ LEFTOVER CHARGE IS NOT AN ERROR AND IS NOT CARRIED. A tenant can end a period over both its allowance and its
        // boost: the check runs BEFORE a turn and a turn's size is only known after it. That overshoot is bounded by one
        // turn, and the balance simply reads zero — inventing a negative balance would bill the next period for it.
        return new AssistantTokenBalance(
            IncludedLimit: includedLimit,
            IncludedUsed: usedInPeriod,
            IncludedRemaining: includedRemaining,
            BoostRemaining: remaining,
            BoostExpired: expired,
            BoostNextExpiry: nextExpiry);
    }
}
