using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;

namespace Wasnie.Infrastructure.Services;

public sealed class StripeCheckoutService(
    IApplicationDbContext db,
    IOptions<StripeOptions> options,
    ILogger<StripeCheckoutService> logger)
    : IStripeCheckoutService
{
    public async Task<string> CreateCheckoutSessionAsync(
        Guid tenantId,
        string priceId,
        string billingEmail,
        CancellationToken cancellationToken = default)
    {
        var client = new StripeClient(options.Value.SecretKey);

        // Retrieve existing Stripe Customer ID if the tenant has one from a previous checkout
        var existingSubscription = await db.UserSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

        var sessionCreateOptions = new SessionCreateOptions
        {
            Mode = "subscription",
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Price = priceId,
                    Quantity = 1,
                },
            ],
            // TenantId goes server-side into metadata — never trusting frontend for webhook activation
            Metadata = new Dictionary<string, string>
            {
                ["tenantId"] = tenantId.ToString(),
            },
            SuccessUrl = $"{options.Value.FrontendBaseUrl}/onboarding/success?session_id={{CHECKOUT_SESSION_ID}}",
            CancelUrl = $"{options.Value.FrontendBaseUrl}/onboarding/plan",
        };

        if (!string.IsNullOrWhiteSpace(existingSubscription?.StripeCustomerId))
            sessionCreateOptions.Customer = existingSubscription.StripeCustomerId;
        else
            sessionCreateOptions.CustomerEmail = billingEmail;

        var sessionService = new SessionService(client);
        var session = await sessionService.CreateAsync(sessionCreateOptions, cancellationToken: cancellationToken);

        logger.LogInformation(
            "Stripe Checkout Session {SessionId} created for tenant {TenantId} with price {PriceId}",
            session.Id, tenantId, priceId);

        return session.Url;
    }
}
