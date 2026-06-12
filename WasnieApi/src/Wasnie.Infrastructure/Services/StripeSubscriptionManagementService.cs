using Microsoft.Extensions.Options;
using Stripe;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;

namespace Wasnie.Infrastructure.Services;

public sealed class StripeSubscriptionManagementService(IOptions<StripeOptions> options)
    : IStripeSubscriptionManagementService
{
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
