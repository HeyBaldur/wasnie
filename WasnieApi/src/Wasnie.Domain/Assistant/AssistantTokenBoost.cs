using Wasnie.Domain.Common;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Assistant;

/// <summary>
/// One batch of assistant tokens a tenant BOUGHT (KAN-83) — a boost.
///
/// ★ A LOT, NOT A BALANCE. The row records a purchase: how many tokens, when, and when they die. What is left of it is
/// derived by walking the lots against consumption (§B5) — there is no "remaining" column to drift out of step with the
/// usage rows. Two customers with the same remaining balance got there differently, and only the lots say how.
///
/// ★ WHAT IS INCLUDED IS NOT A LOT. The plan's monthly allowance resets every period and is a number in configuration,
/// not a row here: it was never bought and never rolls over. Only what the tenant PAID FOR EXTRA survives a renewal,
/// which is the whole distinction this table exists to keep.
///
/// ★ THE STRIPE EVENT ID IS THE IDEMPOTENCY KEY. Stripe redelivers. Crediting a boost twice is giving away tokens;
/// crediting it zero times is taking money for nothing. The unique index on <see cref="StripeEventId"/> makes the
/// second delivery fail to insert rather than double the balance.
///
/// ★ TOKENS COME FROM THE PRODUCT, NOT FROM CODE. How many tokens a boost is worth is read from the Stripe product's
/// <c>metadata.tokens</c> at credit time and frozen here. Adding or repricing a boost is then a change in Stripe, not a
/// deploy — and the row keeps what was actually granted, even if the product's metadata changes later.
/// </summary>
public sealed class AssistantTokenBoost : Entity
{
    public const int MaxStripeIdLength = 255;

    public Guid TenantId { get; private set; }

    /// <summary>The Stripe event that credited this boost. Unique — see the idempotency note above.</summary>
    public string StripeEventId { get; private set; } = string.Empty;

    /// <summary>The Stripe product bought, kept so a credit can be traced back to what the customer saw.</summary>
    public string StripeProductId { get; private set; } = string.Empty;

    /// <summary>The checkout session that paid for it. Null only if a credit ever arrives by another route.</summary>
    public string? StripeSessionId { get; private set; }

    /// <summary>Tokens granted, as read from the product's <c>metadata.tokens</c>. Always positive.</summary>
    public long Tokens { get; private set; }

    public DateTimeOffset PurchasedAt { get; private set; }

    /// <summary>
    /// When what is left of this lot dies. A bought token is a liability on the books; without an end it is an eternal
    /// one. Configured (a year by default), and frozen per lot so changing the setting never shortens a lot already sold.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    private AssistantTokenBoost() { }

    public static AssistantTokenBoost Create(
        Guid id,
        Guid tenantId,
        string stripeEventId,
        string stripeProductId,
        string? stripeSessionId,
        long tokens,
        DateTimeOffset purchasedAt,
        DateTimeOffset expiresAt)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("TenantId must not be empty.");
        if (string.IsNullOrWhiteSpace(stripeEventId))
            throw new DomainException("StripeEventId must not be empty.");
        if (string.IsNullOrWhiteSpace(stripeProductId))
            throw new DomainException("StripeProductId must not be empty.");
        if (tokens <= 0)
            throw new DomainException("A boost must grant a positive number of tokens.");
        if (expiresAt <= purchasedAt)
            throw new DomainException("A boost must expire after it was purchased.");

        return new AssistantTokenBoost
        {
            Id = id,
            TenantId = tenantId,
            StripeEventId = stripeEventId,
            StripeProductId = stripeProductId,
            StripeSessionId = stripeSessionId,
            Tokens = tokens,
            PurchasedAt = purchasedAt,
            ExpiresAt = expiresAt,
        };
    }
}
