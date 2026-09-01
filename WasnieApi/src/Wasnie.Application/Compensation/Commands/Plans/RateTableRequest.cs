using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Commands.Plans;

/// <summary>
/// The wire shape of a rate table on the way IN, and the reason the domain's invariants are now
/// reachable at all.
///
/// ★★ THE COMMANDS USED TO DECLARE <see cref="RateTable"/> ITSELF, AND THAT IS WHAT KILLED THE
/// VALIDATION. Every property of <c>RateTable</c> is <c>init</c> (EF's value converter and
/// <c>RuleSnapshotJsonConverter</c> both depend on that), so <c>[FromBody] AddRuleToPlanCommand</c>
/// let System.Text.Json build the object property by property and hand it straight to
/// <c>Plan.AddRule</c>. <c>RateTable.Tiered</c> and <c>RateTable.AttainmentBased</c> — the only code
/// in the solution that checks a ladder of tiers — were never called anywhere in <c>src/</c>: a grep
/// found the two definitions and nothing else. The ordering and overlap rules that had been sitting
/// in <c>Tiered</c> since the beginning had never once run in production.
///
/// ★ THE FIX IS A SEPARATE TYPE, NOT A CHANGE TO THE DOMAIN ONE. Making <c>RateTable</c> harder to
/// construct would break the read path, which must keep accepting the malformed tables already in
/// the database. Giving the command its own type moves the seam to where it belongs: JSON builds
/// THIS, and <see cref="ToDomain"/> is the only door from here into the domain.
///
/// ★ THE JSON DOES NOT CHANGE. The property names and nesting match what the front already sends
/// and what <c>RateTable</c> already serialises, so no client changes and no stored payload moves.
/// </summary>
public sealed record RateTableRequest(
    RateTableType Type,
    decimal? FlatRate,
    IReadOnlyList<RateTierRequest>? Tiers,
    IReadOnlyList<AttainmentTierRequest>? AttainmentTiers,
    bool SplitAtQuota = false)
{
    /// <summary>
    /// Builds the domain table THROUGH THE FACTORIES, so every invariant runs.
    ///
    /// A broken ladder throws <see cref="DomainCodedException"/> naming the specific invariant and
    /// the tiers involved; the handlers let that one past their catch and
    /// ExceptionHandlingMiddleware turns it into a 422 carrying the code, which the client renders in
    /// the reader's own language. The two refusals below are still plain
    /// <see cref="DomainException"/> prose — they are not ladder invariants and were not part of that
    /// pass.
    /// </summary>
    public RateTable ToDomain() => Type switch
    {
        RateTableType.Flat =>
            RateTable.Flat(FlatRate
                ?? throw new DomainException("A flat rate table requires a rate.")),

        RateTableType.Tiered =>
            RateTable.Tiered((Tiers ?? [])
                .Select(t => new RateTier { From = t.From, To = t.To, Rate = t.Rate })
                .ToList()),

        RateTableType.AttainmentBased =>
            RateTable.AttainmentBased(
                (AttainmentTiers ?? [])
                    .Select(t => new AttainmentTier
                    {
                        AttainmentFrom = t.AttainmentFrom,
                        AttainmentTo = t.AttainmentTo,
                        Rate = t.Rate,
                    })
                    .ToList(),
                SplitAtQuota),

        _ => throw new DomainException($"Unsupported rate table type: {Type}."),
    };
}

/// <summary>One tier of an amount ladder. <c>From</c>/<c>To</c> are MONEY in the plan's currency.</summary>
public sealed record RateTierRequest(decimal From, decimal? To, decimal Rate);

/// <summary>
/// One tier of an attainment ladder. <c>AttainmentFrom</c>/<c>AttainmentTo</c> are RATIOS of quota —
/// 1 is 100% of target, not one unit of currency.
/// </summary>
public sealed record AttainmentTierRequest(decimal AttainmentFrom, decimal? AttainmentTo, decimal Rate);
