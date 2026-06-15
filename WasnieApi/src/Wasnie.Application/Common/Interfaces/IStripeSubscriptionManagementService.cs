using Wasnie.Domain.Authorization;

namespace Wasnie.Application.Common.Interfaces;

public interface IStripeSubscriptionManagementService
{
    /// <summary>
    /// Resolves the current tier of a Stripe subscription from its active price.
    /// Returns null if the subscription has no items or the price cannot be mapped to a known tier.
    /// Throws StripeException on network/API failures — callers must handle this as an abort signal.
    /// </summary>
    Task<Tier?> GetCurrentTierFromStripeAsync(string subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upgrade: charges the full new-plan price immediately and resets the billing cycle to now.
    /// Throws StripeException if the payment is declined (error_if_incomplete).
    /// </summary>
    Task UpgradeSubscriptionAsync(string subscriptionId, string newPriceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downgrade: switches to the new price without an immediate charge.
    /// A proration credit for the unused days is applied to the next invoice.
    /// </summary>
    Task UpdateSubscriptionAsync(string subscriptionId, string newPriceId, CancellationToken cancellationToken = default);

    Task RevertCancellationAsync(string subscriptionId, CancellationToken cancellationToken = default);
    Task<string> CreateBillingPortalSessionAsync(string customerId, string returnUrl, CancellationToken cancellationToken = default);
}
