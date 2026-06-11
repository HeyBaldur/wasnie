using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Domain.Authorization;

namespace Wasnie.Infrastructure.Services;

public sealed class StripeSubscriptionPlanService(
    IOptions<StripeOptions> options,
    ILogger<StripeSubscriptionPlanService> logger)
    : ISubscriptionPlanService
{
    // Stripe metadata key that maps a product to a Wasnie tier.
    private const string TierMetadataKey = "tier";

    // Free plan is synthetic — it has no Stripe product.
    private static readonly SubscriptionPlanDto FreePlan = new(
        PriceId:      null,
        ProductId:    null,
        Name:         "Free",
        Price:        0m,
        Currency:     "eur",
        Interval:     "free",
        Tier:         nameof(Domain.Authorization.Tier.Free),
        MaxPayees:    TierLimits.Limits[Domain.Authorization.Tier.Free].MaxPayees,
        MaxPlans:     TierLimits.Limits[Domain.Authorization.Tier.Free].MaxPlans,
        IsCurrentPlan: false);

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(
        Tier currentTier, CancellationToken cancellationToken = default)
    {
        StripeClient client = new(options.Value.SecretKey);
        PriceService priceService = new(client);

        logger.LogInformation("Fetching active Stripe prices for subscription wizard");

        IEnumerable<Price> prices;
        try
        {
            var listOptions = new PriceListOptions
            {
                Active = true,
                Expand = ["data.product"],
                Type = "recurring",
                Limit = 100,
            };
            StripeList<Price> stripeList = await priceService.ListAsync(listOptions, cancellationToken: cancellationToken);
            prices = stripeList.Data;
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Stripe API call failed while fetching subscription plans");
            throw new StripeUnavailableException(
                "The subscription plan service is temporarily unavailable. Please try again shortly.", ex);
        }

        var paid = prices
            .Select(p => TryMapPrice(p, currentTier))
            .OfType<SubscriptionPlanDto>()
            .OrderBy(p => p.Price)
            .ToList();

        var result = new List<SubscriptionPlanDto>(paid.Count + 1)
        {
            FreePlan with { IsCurrentPlan = currentTier == Domain.Authorization.Tier.Free },
        };
        result.AddRange(paid);

        logger.LogInformation("Returning {Count} subscription plans ({Paid} from Stripe)", result.Count, paid.Count);
        return result;
    }

    private SubscriptionPlanDto? TryMapPrice(Price price, Tier currentTier)
    {
        if (price.Product is not Product product)
            return null;

        var tierSlug = ResolveTier(product.Id, product.Metadata, options.Value.ProductTierMap, logger);
        if (tierSlug is null)
            return null;

        if (!Enum.TryParse<Tier>(tierSlug, ignoreCase: true, out var tier))
        {
            logger.LogWarning(
                "Stripe product {ProductId} '{ProductName}' has unrecognized tier value '{TierSlug}' — discarded",
                product.Id, product.Name, tierSlug);
            return null;
        }

        if (!TierLimits.Limits.TryGetValue(tier, out var limits))
            return null;

        var unitAmount = price.UnitAmount ?? 0;
        var priceAmount = unitAmount / 100m;
        var currency = price.Currency?.ToUpperInvariant() ?? "EUR";
        var interval = price.Recurring?.Interval ?? "month";

        return new SubscriptionPlanDto(
            PriceId:      price.Id,
            ProductId:    product.Id,
            Name:         product.Name,
            Price:        priceAmount,
            Currency:     currency,
            Interval:     interval,
            Tier:         tier.ToString(),
            MaxPayees:    limits.MaxPayees == int.MaxValue ? -1 : limits.MaxPayees,
            MaxPlans:     limits.MaxPlans  == int.MaxValue ? -1 : limits.MaxPlans,
            IsCurrentPlan: tier == currentTier);
    }

    // Resolves the Wasnie tier slug for a Stripe product.
    // Precedence: product.Metadata["tier"] → ProductTierMap[productId] → null + WARNING.
    // Exposed as internal so unit tests can exercise the mapping logic without calling Stripe.
    internal static string? ResolveTier(
        string productId,
        IDictionary<string, string> productMetadata,
        IDictionary<string, string> productTierMap,
        ILogger logger)
    {
        // 1. Metadata — takes precedence; forward-compatible if user adds "tier" key to Stripe later.
        if (productMetadata.TryGetValue(TierMetadataKey, out var metadataTier)
            && !string.IsNullOrWhiteSpace(metadataTier))
            return metadataTier;

        // 2. Config map — primary path for products whose Stripe metadata is empty.
        if (productTierMap.TryGetValue(productId, out var configTier)
            && !string.IsNullOrWhiteSpace(configTier))
            return configTier;

        // 3. Neither source has a mapping — log clearly so the operator knows what to fix.
        logger.LogWarning(
            "Stripe product {ProductId} has no tier in product metadata and is not in Stripe:ProductTierMap — discarded. " +
            "Add its ProductId to Stripe:ProductTierMap in appsettings to include it in the plans list.",
            productId);
        return null;
    }
}
