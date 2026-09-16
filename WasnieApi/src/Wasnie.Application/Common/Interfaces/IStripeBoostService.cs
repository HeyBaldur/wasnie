namespace Wasnie.Application.Common.Interfaces;

/// <summary>One boost pack as it is offered to a tenant (KAN-83).</summary>
/// <param name="PriceId">The Stripe price to check out with.</param>
/// <param name="ProductId">The Stripe product it belongs to.</param>
/// <param name="Tokens">Tokens the pack grants, read from the product's <c>metadata.tokens</c>.</param>
/// <param name="AmountCents">What it costs, in the currency's smallest unit.</param>
/// <param name="Currency">ISO currency code, lowercase as Stripe returns it.</param>
public sealed record BoostOffer(string PriceId, string ProductId, long Tokens, long AmountCents, string Currency);

/// <summary>
/// Buying extra assistant tokens (KAN-83). A ONE-OFF payment, not a second subscription.
///
/// ★ THE PACKS ARE DEFINED IN STRIPE, NOT HERE. Which prices are on sale is configuration
/// (<c>Billing:Boosts:PriceIds</c>); how many tokens each grants and what it costs are read from Stripe itself. Adding a
/// pack or changing a size is then a change in Stripe plus one line of config — never a deploy.
///
/// ★ A PACK WITH NO READABLE <c>metadata.tokens</c> IS NOT OFFERED. Selling a pack whose size we would have to guess is
/// how a customer pays for tokens nobody agreed on; leaving it out of the list is the honest failure.
/// </summary>
public interface IStripeBoostService
{
    /// <summary>The packs on sale, smallest first. Empty when none are configured or Stripe is unreachable.</summary>
    Task<IReadOnlyList<BoostOffer>> GetOffersAsync(CancellationToken cancellationToken = default);

    /// <summary>A Stripe Checkout session in <c>payment</c> mode for one pack. Returns the URL to send the user to.</summary>
    /// <param name="returnTo">
    /// Which screen the customer came from, so Checkout returns them there rather than always to billing.
    ///
    /// ★★ AN ENUM, NEVER A URL FROM THE CLIENT. A caller-supplied return address on a payment flow is an open
    /// redirect: it would let a link send someone to pay and land them on an attacker's page still trusting they are
    /// in Incentra. The client says WHICH screen; this side decides what that spells.
    /// </param>
    Task<string> CreateBoostCheckoutSessionAsync(
        Guid tenantId, string priceId, string billingEmail, BoostReturnTo returnTo = BoostReturnTo.Billing,
        CancellationToken cancellationToken = default);
}

/// <summary>Where a finished boost checkout sends the customer back to (KAN-83).</summary>
public enum BoostReturnTo
{
    /// <summary>Manage billing — the default, and where a purchase started from the billing screen belongs.</summary>
    Billing,

    /// <summary>The assistant. A purchase started from a blocked chat returns to that chat, not to settings.</summary>
    Assistant,
}
