using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Authorization;

namespace Wasnie.Infrastructure.Services;

public sealed class StripeSubscriptionManagementService(
    IOptions<StripeOptions> options,
    ILogger<StripeSubscriptionManagementService> logger)
    : IStripeSubscriptionManagementService
{
    public async Task<Tier?> GetCurrentTierFromStripeAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var client = new StripeClient(options.Value.SecretKey);
        var service = new SubscriptionService(client);
        var subscription = await service.GetAsync(
            subscriptionId,
            new SubscriptionGetOptions { Expand = ["items.data.price.product"] },
            cancellationToken: cancellationToken);

        var price = subscription.Items?.Data?.FirstOrDefault()?.Price;
        if (price?.Product is not Product product)
            return null;

        var tierSlug = StripeSubscriptionPlanService.ResolveTier(
            product.Id,
            product.Metadata,
            options.Value.ProductTierMap,
            logger);

        if (tierSlug is null)
            return null;

        return Enum.TryParse<Tier>(tierSlug, ignoreCase: true, out var tier) ? tier : null;
    }

    public async Task<Subscription> GetSubscriptionWithProductAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var client = new StripeClient(options.Value.SecretKey);
        var service = new SubscriptionService(client);
        return await service.GetAsync(
            subscriptionId,
            new SubscriptionGetOptions { Expand = ["items.data.price.product"] },
            cancellationToken: cancellationToken);
    }

    public async Task UpgradeSubscriptionAsync(
        string subscriptionId,
        string newPriceId,
        CancellationToken cancellationToken = default)
    {
        var client = new StripeClient(options.Value.SecretKey);
        var service = new SubscriptionService(client);

        var current = await service.GetAsync(
            subscriptionId,
            new SubscriptionGetOptions { Expand = ["items.data"] },
            cancellationToken: cancellationToken);

        var itemId = current.Items?.Data?.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException($"Subscription {subscriptionId} has no items.");

        // Charge the full new-plan price immediately and start a fresh billing cycle from now.
        // payment_behavior=error_if_incomplete makes Stripe reject the API call synchronously
        // if the card cannot be charged, so the subscription is never changed on a failed payment.
        await service.UpdateAsync(
            subscriptionId,
            new SubscriptionUpdateOptions
            {
                Items =
                [
                    new SubscriptionItemOptions
                    {
                        Id = itemId,
                        Price = newPriceId,
                    }
                ],
                ProrationBehavior = "none",
                BillingCycleAnchor = SubscriptionBillingCycleAnchor.Now,
                PaymentBehavior = "error_if_incomplete",
            },
            cancellationToken: cancellationToken);
    }

    public async Task UpdateSubscriptionAsync(
        string subscriptionId,
        string newPriceId,
        CancellationToken cancellationToken = default)
    {
        var client = new StripeClient(options.Value.SecretKey);
        var service = new SubscriptionService(client);

        var current = await service.GetAsync(
            subscriptionId,
            new SubscriptionGetOptions { Expand = ["items.data"] },
            cancellationToken: cancellationToken);

        var itemId = current.Items?.Data?.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException($"Subscription {subscriptionId} has no items.");

        // Downgrade: apply proration credit for unused days to the next invoice, no immediate charge.
        await service.UpdateAsync(
            subscriptionId,
            new SubscriptionUpdateOptions
            {
                Items =
                [
                    new SubscriptionItemOptions
                    {
                        Id = itemId,
                        Price = newPriceId,
                    }
                ],
                ProrationBehavior = "create_prorations",
            },
            cancellationToken: cancellationToken);
    }

    public async Task RevertCancellationAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var client = new StripeClient(options.Value.SecretKey);
        var service = new SubscriptionService(client);
        await service.UpdateAsync(
            subscriptionId,
            new SubscriptionUpdateOptions { CancelAtPeriodEnd = false },
            cancellationToken: cancellationToken);
    }

    public async Task<string> CreateBillingPortalSessionAsync(
        string customerId,
        string returnUrl,
        CancellationToken cancellationToken = default)
    {
        var client = new StripeClient(options.Value.SecretKey);
        var service = new Stripe.BillingPortal.SessionService(client);

        var session = await service.CreateAsync(
            new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = customerId,
                ReturnUrl = returnUrl,
            },
            cancellationToken: cancellationToken);

        return session.Url;
    }
}
