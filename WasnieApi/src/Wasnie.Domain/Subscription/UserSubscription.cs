using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common;

namespace Wasnie.Domain.Subscription;

public sealed class UserSubscription : AggregateRoot
{
    public Guid TenantId { get; private set; }
    /// <summary>LEGACY (pre-KAN-77). Kept in the database untouched; no decision reads it. See <see cref="PlanCode"/>.</summary>
    public Tier Tier { get; private set; }

    /// <summary>The plan this Stripe subscription is for (a code from Billing:Plans). Null before checkout completes.</summary>
    public string? PlanCode { get; private set; }
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

    /// <summary>
    /// A subscription row before Stripe has confirmed anything — the webhook completes it with
    /// <see cref="UpdateFromStripe"/> in the same unit of work.
    ///
    /// ★ KAN-77: this used to be <c>CreateFree</c> and wrote Status = Active with no Stripe id, which made a free
    /// row indistinguishable from a paying one to anything that only looked at Status. The free plan is gone; a
    /// row that nothing has confirmed is Incomplete, which is what it is.
    /// </summary>
    public static UserSubscription CreatePending(Guid id, Guid tenantId, string billingEmail, DateTimeOffset now) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            Status = SubscriptionStatus.Incomplete,
            BillingEmail = billingEmail,
            CreatedAt = now,
            UpdatedAt = now,
        };

    // Called by Stripe webhooks in Fase 3 when a paid subscription is activated/updated.
    // Always resets cancellation fields: a freshly activated subscription has no pending
    // cancellation and is not in a canceled state. Callers that need to re-apply a
    // cancellation schedule (subscription.updated) do so explicitly after this call.
    public void UpdateFromStripe(
        string planCode,
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
        PlanCode = planCode;
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

    /// <param name="canceledAt">When the subscription actually ended, if known. A sync that notices a missed
    /// cancellation days later must record the day it ended, not the day it was noticed (§B6).</param>
    public void Cancel(DateTimeOffset now, DateTimeOffset? canceledAt = null)
    {
        Status = SubscriptionStatus.Canceled;
        CanceledAt = canceledAt ?? now;
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

    // Corrects a stale DB plan to match Stripe's authoritative value.
    // Called by ChangePlanCommandHandler before deciding upgrade vs downgrade,
    // to close the window between a Stripe webhook arriving and the DB being updated.
    public void SyncPlan(string planCode, DateTimeOffset now)
    {
        PlanCode = planCode;
        UpdatedAt = now;
    }
}
