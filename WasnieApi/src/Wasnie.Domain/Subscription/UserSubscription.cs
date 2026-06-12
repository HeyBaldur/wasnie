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

    // cancel_at_period_end: subscription is Active but will not renew
    public bool CancelAtPeriodEnd { get; private set; }
    public DateTimeOffset? CancelAt { get; private set; }

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

    // Called by Stripe webhooks in Fase 3 when a paid subscription is activated/updated.
    // Always resets cancellation fields: a freshly activated subscription has no pending
    // cancellation and is not in a canceled state. Callers that need to re-apply a
    // cancellation schedule (subscription.updated) do so explicitly after this call.
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
        CancelAtPeriodEnd = false;
        CancelAt = null;
        CanceledAt = null;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        Status = SubscriptionStatus.Canceled;
        CanceledAt = now;
        CancelAtPeriodEnd = false;
        CancelAt = null;
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
        CanceledAt = null;
        UpdatedAt = now;
    }

    public void ScheduleCancellation(DateTimeOffset cancelAt, DateTimeOffset now)
    {
        CancelAtPeriodEnd = true;
        CancelAt = cancelAt;
        UpdatedAt = now;
    }

    public void ClearCancellationSchedule(DateTimeOffset now)
    {
        CancelAtPeriodEnd = false;
        CancelAt = null;
        UpdatedAt = now;
    }
}
