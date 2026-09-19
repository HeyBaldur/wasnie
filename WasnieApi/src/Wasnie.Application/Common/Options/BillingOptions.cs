namespace Wasnie.Application.Common.Options;

/// <summary>
/// The commercial knobs of KAN-77, kept in configuration so they change without a deploy of code.
///
/// ★ BOUND WITH ValidateOnStart. Unlike the model keys, a wrong value here is not something to degrade
/// around: a trial of 0 days locks every new account on arrival, a limit of 0 disables the assistant for
/// every trial, and a default plan that is not in the catalog leaves trials with no limits to read.
/// Failing at start-up is the honest outcome.
/// </summary>
public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>Length of the free trial granted at registration, in days.</summary>
    public int TrialDays { get; init; } = 7;

    /// <summary>
    /// How many assistant TOKENS (input + output) a tenant may consume during its trial, across all its users (KAN-80).
    ///
    /// ★ TOKENS, NOT MESSAGES. The old limit was 300 messages, and a message costs anywhere from a few hundred tokens to
    /// tens of thousands — so it measured nothing that costs money. Tokens are what the model provider bills. Paying
    /// tenants have no limit.
    /// </summary>
    public long TrialAssistantTokenLimit { get; init; } = 500_000;

    /// <summary>
    /// How many assistant TOKENS a PAYING tenant's plan includes per billing period, shared by all its users (KAN-83).
    ///
    /// ★ IT RESETS, IT DOES NOT ROLL OVER. This is the usage the plan grants for the period, not savings: every renewal
    /// starts at this number again, and what went unused is gone. Only a purchased boost survives a renewal — that is
    /// the whole difference between this setting and an <c>AssistantTokenBoost</c> lot.
    ///
    /// ★ THE GUARD EXISTS BECAUSE THE PRICE IS FLAT. €299 with unlimited users means every token a tenant's reps spend
    /// comes out of the margin. This is the ceiling that makes opening the assistant to everyone safe.
    /// </summary>
    public long IncludedAssistantTokensPerMonth { get; init; } = 3_000_000;

    /// <summary>The boost packs on sale, and how long a bought token lives. See <see cref="AssistantBoostOptions"/>.</summary>
    public AssistantBoostOptions Boosts { get; init; } = new();

    /// <summary>
    /// The plan a trial experiences, and the plan a LEGACY Stripe product (one still carrying the old
    /// <c>metadata.tier</c> of Starter/Growth/Scale) is honoured as — so a customer paying on an old price is
    /// never left without a plan. Must be the <see cref="SubscriptionPlanDefinition.Code"/> of a plan below.
    /// </summary>
    public string DefaultPlanCode { get; init; } = "pro";

    /// <summary>
    /// ★ THE PLAN CATALOG. Today one plan; adding a second one is adding an entry here (and its Stripe product),
    /// not changing code — which is the KAN-77 acceptance criterion this list exists for.
    /// </summary>
    public List<SubscriptionPlanDefinition> Plans { get; init; } = [];
}

/// <summary>
/// The boost packs a tenant can buy when its included tokens run out (KAN-83).
///
/// ★ PRICE IDS HERE, TOKEN AMOUNTS IN STRIPE. This lists WHICH packs are on sale; how many tokens each one grants is
/// read from the Stripe product's <c>metadata.tokens</c>, never mapped from a price id in code. Adding a pack, changing
/// its size or repricing it is then a change in Stripe plus one line of configuration — no deploy. A pack whose product
/// carries no usable <c>metadata.tokens</c> is refused rather than guessed at: crediting an invented number would hand
/// out tokens nobody sold.
/// </summary>
public sealed class AssistantBoostOptions
{
    /// <summary>Stripe PRICE ids of the packs on sale, in the order they are offered. Empty = boosts not for sale.</summary>
    public List<string> PriceIds { get; init; } = [];

    /// <summary>
    /// How long a bought token lives, in days. A year by default, which is generous and still keeps the liability from
    /// being eternal. Frozen onto each lot at purchase, so changing it never shortens a boost already sold.
    /// </summary>
    public int ExpiryDays { get; init; } = 365;

    /// <summary>The Stripe product metadata key holding a pack's token count.</summary>
    public const string TokensMetadataKey = "tokens";
}

/// <summary>One subscription plan Incentra sells.</summary>
public sealed class SubscriptionPlanDefinition
{
    /// <summary>Stable identifier stored on tenants and subscriptions (e.g. "pro"). Never a display name.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Stripe products that ARE this plan, for products whose metadata does not say so. A product that carries
    /// <c>metadata.plan</c> needs no entry here.
    /// </summary>
    public List<string> StripeProductIds { get; init; } = [];

    /// <summary>Maximum payees. Null = unlimited.</summary>
    public int? MaxPayees { get; init; }

    /// <summary>Maximum compensation plans. Null = unlimited.</summary>
    public int? MaxPlans { get; init; }

    /// <summary>
    /// Maximum people with a login in this tenant. Null = unlimited, and null is what every plan ships
    /// with today (KAN-32).
    ///
    /// ★★ THE SEAT IS ACCESS CONTROL, NOT A BILLING UNIT, and that was decided rather than assumed:
    /// the €299 plan of KAN-77 includes unlimited users, so nothing here is charged for. The parameter
    /// exists so that the day a tier DOES want a cap it is one line of configuration instead of a
    /// module rewrite — which is exactly what the 2026-09-02 note on that ticket asked for.
    ///
    /// ★ IT DOES NOT REPLACE <see cref="MaxPayees"/>, IT SITS BESIDE IT. A payee is a person who gets
    /// paid; a user is a person who can sign in, and most payees are neither. Folding the two counts
    /// together would charge twice for the one employee who is both.
    ///
    /// ★ NULL MEANS UNLIMITED HERE, following MaxPayees and MaxPlans above. Deliberately NOT the
    /// fail-closed treatment given to Assistant:Provider: an unset provider means nobody chose, while
    /// an unset cap means there is no cap — and refusing to let anyone in because a limit nobody wants
    /// was left blank would be inventing a restriction out of silence.
    /// </summary>
    public int? MaxUsers { get; init; }
}
