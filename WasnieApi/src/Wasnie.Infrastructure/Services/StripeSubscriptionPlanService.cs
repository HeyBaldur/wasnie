using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Subscription;
using Wasnie.Application.Features.Subscription.DTOs;

namespace Wasnie.Infrastructure.Services;

/// <summary>
/// The plans a tenant can buy, read live from Stripe and matched against the plan catalog (KAN-77).
///
/// ★ NO SYNTHETIC FREE PLAN. The free plan is gone; the list holds only what can be paid for.
///
/// ★★ A PRICE IS OFFERED ONLY IF BOTH THE PRICE AND ITS PRODUCT ARE ACTIVE. Archiving a product in Stripe does
/// NOT archive its prices: on 2026-09-14 Growth and Scale had their products archived and their prices still
/// active, and this service — which only asked Stripe for active prices — kept offering both. A checkout
/// against an archived product is not something to find out at the payment step.
/// </summary>
public sealed class StripeSubscriptionPlanService(
    IOptions<StripeOptions> options,
    ISubscriptionPlanCatalog catalog,
    ILogger<StripeSubscriptionPlanService> logger)
    : ISubscriptionPlanService
{
    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(
        string? currentPlanCode, CancellationToken cancellationToken = default)
    {
        StripeClient client = new(options.Value.SecretKey);
        PriceService priceService = new(client);

        logger.LogInformation("Fetching active Stripe prices for the plan list");

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

        var plans = prices
            .Select(p => TryMapPrice(p, currentPlanCode))
            .OfType<SubscriptionPlanDto>()
            .OrderBy(p => p.Price)
            .ToList();

        logger.LogInformation("Returning {Count} subscription plan(s) from Stripe", plans.Count);
        return plans;
    }

    private SubscriptionPlanDto? TryMapPrice(Price price, string? currentPlanCode)
    {
        if (price.Product is not Product product)
            return null;

        if (!product.Active)
        {
            logger.LogInformation(
                "Stripe price {PriceId} belongs to archived product {ProductId} '{ProductName}' — not offered",
                price.Id, product.Id, product.Name);
            return null;
        }

        var plan = catalog.ResolveStripeProduct(product.Id, product.Metadata);
        if (plan is null)
            return null;

        return new SubscriptionPlanDto(
            PriceId:       price.Id,
            ProductId:     product.Id,
            Name:          product.Name,
            Price:         (price.UnitAmount ?? 0) / 100m,
            Currency:      price.Currency?.ToUpperInvariant() ?? "EUR",
            Interval:      price.Recurring?.Interval ?? "month",
            PlanCode:      plan.Code,
            MaxPayees:     plan.MaxPayees ?? -1,
            MaxPlans:      plan.MaxPlans ?? -1,
            IsCurrentPlan: string.Equals(plan.Code, currentPlanCode, StringComparison.OrdinalIgnoreCase));
    }
}
