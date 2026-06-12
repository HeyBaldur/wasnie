namespace Wasnie.Application.Common.Interfaces;

public interface IStripeSubscriptionManagementService
{
    Task UpdateSubscriptionAsync(string subscriptionId, string newPriceId, CancellationToken cancellationToken = default);
    Task RevertCancellationAsync(string subscriptionId, CancellationToken cancellationToken = default);
    Task<string> CreateBillingPortalSessionAsync(string customerId, string returnUrl, CancellationToken cancellationToken = default);
}
