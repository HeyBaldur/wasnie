using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Options;

namespace Wasnie.Application.Features.Subscription;

/// <summary>
/// ★ THE ONE PLACE that knows which plans exist and which Stripe product is which plan (KAN-77). It replaces the
/// hard-coded <c>Tier</c> enum + <c>TierLimits</c> table: plans and their limits are configuration
/// (<see cref="BillingOptions.Plans"/>), so selling a second plan is a config change, not a refactor.
/// </summary>
public interface ISubscriptionPlanCatalog
{
    /// <summary>The plan trials use and legacy subscriptions are honoured as.</summary>
    SubscriptionPlanDefinition Default { get; }

    IReadOnlyList<SubscriptionPlanDefinition> All { get; }

    /// <summary>The plan with that code, or null. Case-insensitive.</summary>
    SubscriptionPlanDefinition? Find(string? code);

    /// <summary>
    /// Which plan a Stripe product is, or null when it is not one we sell (never granted anything).
    /// Precedence: <c>metadata.plan</c> → <see cref="SubscriptionPlanDefinition.StripeProductIds"/> → a LEGACY
    /// product carrying the old <c>metadata.tier</c> → <see cref="Default"/>.
    /// </summary>
    SubscriptionPlanDefinition? ResolveStripeProduct(string productId, IDictionary<string, string>? metadata);

    /// <summary>
    /// True when moving from <paramref name="from"/> to <paramref name="to"/> gives MORE room (payees or plans;
    /// unlimited is the most). Unknown origin counts as an upgrade. The one definition of direction, shared by the
    /// webhook audit and the plan change.
    /// </summary>
    bool IsUpgrade(SubscriptionPlanDefinition? from, SubscriptionPlanDefinition to);
}

public sealed class SubscriptionPlanCatalog(IOptions<BillingOptions> options, ILogger<SubscriptionPlanCatalog> logger)
    : ISubscriptionPlanCatalog
{
    public const string PlanMetadataKey = "plan";

    /// <summary>The metadata key of the pre-KAN-77 tiers. Read only to keep paying legacy customers covered.</summary>
    public const string LegacyTierMetadataKey = "tier";

    private BillingOptions Options => options.Value;

    public IReadOnlyList<SubscriptionPlanDefinition> All => Options.Plans;

    public SubscriptionPlanDefinition Default =>
        Find(Options.DefaultPlanCode)
        ?? throw new InvalidOperationException(
            $"Billing:DefaultPlanCode '{Options.DefaultPlanCode}' is not in Billing:Plans (validated at start-up).");

    public SubscriptionPlanDefinition? Find(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? null
            : Options.Plans.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));

    public SubscriptionPlanDefinition? ResolveStripeProduct(string productId, IDictionary<string, string>? metadata)
    {
        // 1. The product says which plan it is.
        if (metadata is not null
            && metadata.TryGetValue(PlanMetadataKey, out var planCode)
            && !string.IsNullOrWhiteSpace(planCode))
        {
            var byMetadata = Find(planCode);
            if (byMetadata is null)
                logger.LogWarning(
                    "Stripe product {ProductId} has metadata.plan '{PlanCode}', which is not in Billing:Plans — ignored.",
                    productId, planCode);
            return byMetadata;
        }

        // 2. The catalog lists the product.
        var byProductId = Options.Plans.FirstOrDefault(p =>
            p.StripeProductIds.Contains(productId, StringComparer.Ordinal));
        if (byProductId is not null)
            return byProductId;

        // 3. ★ A LEGACY product (Starter/Growth/Scale, from before KAN-77). Someone may still be PAYING on it,
        // and "whoever pays is not interrupted": they are honoured as the default plan rather than dropped.
        if (metadata is not null
            && metadata.TryGetValue(LegacyTierMetadataKey, out var legacyTier)
            && !string.IsNullOrWhiteSpace(legacyTier))
        {
            logger.LogInformation(
                "Stripe product {ProductId} is a legacy tier '{Tier}' — honoured as plan '{Plan}'.",
                productId, legacyTier, Options.DefaultPlanCode);
            return Default;
        }

        logger.LogWarning(
            "Stripe product {ProductId} is not a known plan (no metadata.plan, not in Billing:Plans) — ignored.",
            productId);
        return null;
    }

    public bool IsUpgrade(SubscriptionPlanDefinition? from, SubscriptionPlanDefinition to) =>
        from is null
        || (to.MaxPayees ?? int.MaxValue) > (from.MaxPayees ?? int.MaxValue)
        || (to.MaxPlans ?? int.MaxValue) > (from.MaxPlans ?? int.MaxValue);

    /// <summary>Start-up validation of the catalog. Returns the problems found; empty means valid.</summary>
    public static IEnumerable<string> Validate(BillingOptions o)
    {
        if (o.Plans.Count == 0)
            yield return "Billing:Plans must define at least one plan.";

        if (o.Plans.Any(p => string.IsNullOrWhiteSpace(p.Code)))
            yield return "Every Billing:Plans entry needs a Code.";

        var duplicate = o.Plans.GroupBy(p => p.Code, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            yield return $"Billing:Plans has the code '{duplicate.Key}' more than once.";

        if (!o.Plans.Any(p => string.Equals(p.Code, o.DefaultPlanCode, StringComparison.OrdinalIgnoreCase)))
            yield return $"Billing:DefaultPlanCode '{o.DefaultPlanCode}' is not one of Billing:Plans.";

        if (o.Plans.Any(p => p.MaxPayees is <= 0 || p.MaxPlans is <= 0))
            yield return "Billing:Plans limits must be positive, or null for unlimited.";
    }
}
