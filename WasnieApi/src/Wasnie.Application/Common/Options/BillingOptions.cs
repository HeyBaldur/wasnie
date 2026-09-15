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
}
