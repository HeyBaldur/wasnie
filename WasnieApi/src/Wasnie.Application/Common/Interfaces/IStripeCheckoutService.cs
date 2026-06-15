namespace Wasnie.Application.Common.Interfaces;

public interface IStripeCheckoutService
{
    Task<string> CreateCheckoutSessionAsync(
        Guid tenantId,
        string priceId,
        string billingEmail,
        CancellationToken cancellationToken = default);
}
