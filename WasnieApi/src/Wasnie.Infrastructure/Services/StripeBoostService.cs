using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;

namespace Wasnie.Infrastructure.Services;

/// <summary>
/// Stripe's half of buying a boost (KAN-83): listing the packs, and starting a ONE-OFF checkout for one.
/// </summary>
public sealed class StripeBoostService(
    IApplicationDbContext db,
    IOptions<StripeOptions> stripeOptions,
    IOptions<BillingOptions> billingOptions,
    ILogger<StripeBoostService> logger)
    : IStripeBoostService
{
    /// <summary>Marks a checkout session as buying tokens. The webhook reads it to tell the two flows apart.</summary>
    public const string BoostMetadataKey = "kind";
    public const string BoostMetadataValue = "boost";

    public async Task<IReadOnlyList<BoostOffer>> GetOffersAsync(CancellationToken cancellationToken = default)
    {
        var priceIds = billingOptions.Value.Boosts.PriceIds;
        if (priceIds.Count == 0)
            return [];

        var client = new StripeClient(stripeOptions.Value.SecretKey);
        var priceService = new PriceService(client);
        var offers = new List<BoostOffer>();

        foreach (var priceId in priceIds)
        {
            Price price;
            try
            {
                price = await priceService.GetAsync(
                    priceId, new PriceGetOptions { Expand = ["product"] }, cancellationToken: cancellationToken);
            }
            catch (StripeException ex)
            {
                // ★ ONE UNREADABLE PACK MUST NOT EMPTY THE SHOP. The others are still on sale, and a tenant out of
                // tokens needs to be able to buy something. The gap is logged so it is not silent (§B1).
                logger.LogError(ex, "Boost price {PriceId} could not be read from Stripe; it is not being offered", priceId);
                continue;
            }

            if (price.Product is not Product product)
            {
                logger.LogError("Boost price {PriceId} has no product; it is not being offered", priceId);
                continue;
            }

            if (!TryReadTokens(product, out var tokens))
            {
                // ★ NEVER GUESS THE SIZE OF A PACK. Selling one whose token count we invented would take money for an
                // amount nobody agreed on — refusing to offer it is the honest failure.
                logger.LogError(
                    "Boost product {ProductId} has no usable '{Key}' metadata; price {PriceId} is not being offered",
                    product.Id, AssistantBoostOptions.TokensMetadataKey, priceId);
                continue;
            }

            offers.Add(new BoostOffer(
                PriceId: price.Id,
                ProductId: product.Id,
                Tokens: tokens,
                AmountCents: price.UnitAmount ?? 0,
                Currency: price.Currency ?? string.Empty));
        }

        return offers.OrderBy(o => o.Tokens).ToList();
    }

    public async Task<string> CreateBoostCheckoutSessionAsync(
        Guid tenantId, string priceId, string billingEmail, BoostReturnTo returnTo = BoostReturnTo.Billing,
        CancellationToken cancellationToken = default)
    {
        var client = new StripeClient(stripeOptions.Value.SecretKey);

        var existing = await db.UserSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

        var options = new SessionCreateOptions
        {
            // ★★ PAYMENT, NOT SUBSCRIPTION. A boost is bought once; billing it monthly would charge a customer every
            // month for tokens they meant to buy a single time.
            Mode = "payment",
            LineItems = [new SessionLineItemOptions { Price = priceId, Quantity = 1 }],
            // ★ THE TENANT COMES FROM THE SERVER, never from the client — the webhook credits whoever this says.
            // `kind` is what lets the webhook tell a boost from a subscription checkout; both arrive as the same event.
            Metadata = new Dictionary<string, string>
            {
                ["tenantId"] = tenantId.ToString(),
                [BoostMetadataKey] = BoostMetadataValue,
            },
            // ★ BACK WHERE THEY STARTED. Someone whose chat stopped mid-question bought tokens to finish that
            // question; landing them in billing settings makes them navigate back to it themselves.
            SuccessUrl = $"{stripeOptions.Value.FrontendBaseUrl}{ReturnPath(returnTo)}?tokens=success",
            CancelUrl = $"{stripeOptions.Value.FrontendBaseUrl}{ReturnPath(returnTo)}",
        };

        if (!string.IsNullOrWhiteSpace(existing?.StripeCustomerId))
            options.Customer = existing.StripeCustomerId;
        else
            options.CustomerEmail = billingEmail;

        var session = await new SessionService(client).CreateAsync(options, cancellationToken: cancellationToken);

        logger.LogInformation(
            "Boost checkout session {SessionId} created for tenant {TenantId} with price {PriceId}",
            session.Id, tenantId, priceId);

        return session.Url;
    }

    /// <summary>
    /// The path a finished checkout returns to. Spelled HERE, from a closed set — the client never supplies a URL.
    /// </summary>
    private static string ReturnPath(BoostReturnTo returnTo) => returnTo switch
    {
        BoostReturnTo.Assistant => "/assistant",
        _ => "/billing",
    };

    /// <summary>
    /// The pack's size, from the product's metadata.
    ///
    /// ★ STRICT ON PURPOSE. Anything that is not a positive whole number is treated as absent rather than coerced:
    /// "3M", "3 000 000" and an empty string would each quietly become some number, and the customer would be credited
    /// it. <see cref="CultureInfo.InvariantCulture"/> so a server's locale cannot change what a pack is worth.
    /// </summary>
    public static bool TryReadTokens(Product product, out long tokens)
    {
        tokens = 0;

        if (product.Metadata is null
            || !product.Metadata.TryGetValue(AssistantBoostOptions.TokensMetadataKey, out var raw))
            return false;

        return long.TryParse(raw, System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture, out tokens)
               && tokens > 0;
    }
}
