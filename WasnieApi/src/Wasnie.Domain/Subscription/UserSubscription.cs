using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common;

namespace Wasnie.Domain.Subscription;

public sealed class UserSubscription : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Tier Tier { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public string BillingEmail { get; private set; } = string.Empty;

    // Stripe identifiers — null for Free plan, populated by webhooks in Fase 3
    public string? StripeSubscriptionId { get; private set; }
    public string? StripeCustomerId { get; private set; }
    public string? StripePriceId { get; private set; }
    public string? StripeProductId { get; private set; }

    // Billing cycle — null for Free plan
    public DateTimeOffset? CurrentPeriodStart { get; private set; }
    public DateTimeOffset? CurrentPeriodEnd { get; private set; }
    public DateTimeOffset? NextBillingDate { get; private set; }
    public DateTimeOffset? CanceledAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private UserSubscription() { }

    public static UserSubscription CreateFree(Guid id, Guid tenantId, string billingEmail, DateTimeOffset now) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            Tier = Tier.Free,
            Status = SubscriptionStatus.Active,
            BillingEmail = billingEmail,
            CreatedAt = now,
            UpdatedAt = now,
        };

    // Called by Stripe webhooks in Fase 3 when a paid subscription is activated/updated
    public void UpdateFromStripe(
        Tier tier,
        SubscriptionStatus status,
        string stripeSubscriptionId,
        string stripeCustomerId,
        string stripePriceId,
        string stripeProductId,
        DateTimeOffset? periodStart,
        DateTimeOffset? periodEnd,
        DateTimeOffset? nextBillingDate,
        DateTimeOffset now)
    {
        Tier = tier;
        Status = status;
        StripeSubscriptionId = stripeSubscriptionId;
        StripeCustomerId = stripeCustomerId;
        StripePriceId = stripePriceId;
        StripeProductId = stripeProductId;
        CurrentPeriodStart = periodStart;
        CurrentPeriodEnd = periodEnd;
        NextBillingDate = nextBillingDate;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        Status = SubscriptionStatus.Canceled;
        CanceledAt = now;
        UpdatedAt = now;
    }

    public void MarkPastDue(DateTimeOffset now)
    {
        Status = SubscriptionStatus.PastDue;
        UpdatedAt = now;
    }

    public void Recover(DateTimeOffset now)
    {
        Status = SubscriptionStatus.Active;
        UpdatedAt = now;
    }
}
