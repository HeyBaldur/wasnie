using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Subscription;

namespace Wasnie.Application.Features.Subscription;

/// <summary>
/// Stripe's subscription status (snake_case) → the stored status. ONE mapping, shared by the webhook and the
/// on-read sync, so the two can never disagree about what "incomplete_expired" means.
/// </summary>
public static class StripeSubscriptionStatus
{
    public static SubscriptionStatus Map(string status) => status switch
    {
        "active" => SubscriptionStatus.Active,
        "past_due" => SubscriptionStatus.PastDue,
        "canceled" => SubscriptionStatus.Canceled,
        "incomplete" => SubscriptionStatus.Incomplete,
        "incomplete_expired" => SubscriptionStatus.Incomplete,
        "trialing" => SubscriptionStatus.Trialing,
        _ => SubscriptionStatus.Active,
    };

    /// <summary>After these the subscription no longer bills or renews: the account has no paid access.</summary>
    public static bool IsEnded(string status) => status is "canceled" or "incomplete_expired";

    /// <summary>Statuses that give access — the same three AccountAccessPolicy lets in.</summary>
    public static bool IsLive(string status) => status is "active" or "trialing" or "past_due";

    /// <summary>
    /// ★ WHICH OF A TENANT'S STRIPE SUBSCRIPTIONS THE ACCOUNT FOLLOWS: the most recent one that gives access. A customer
    /// can hold an old cancelled subscription and a new paid one (KAN-77, runtime); the paid one must win, whatever id
    /// the database happens to hold. Null when none is live.
    /// </summary>
    public static string? PickLive(IEnumerable<(string Id, string Status, DateTime Created)> candidates) =>
        candidates
            .Where(c => IsLive(c.Status))
            .OrderByDescending(c => c.Created)
            .Select(c => c.Id)
            .FirstOrDefault();
}

/// <summary>
/// Whether the stored subscription row has drifted from what Stripe says NOW (KAN-77, runtime: a missed
/// <c>customer.subscription.deleted</c> left a tenant Active — with access — for a subscription Stripe had canceled).
/// </summary>
public static class StripeSubscriptionDrift
{
    /// <summary>Stripe stores seconds; the row may carry sub-second noise from conversions.</summary>
    private static readonly TimeSpan PeriodTolerance = TimeSpan.FromMinutes(1);

    public static bool Differs(UserSubscription row, StripeSubscriptionSnapshot live)
    {
        if (StripeSubscriptionStatus.IsEnded(live.Status))
            return row.Status != SubscriptionStatus.Canceled;

        if (row.Status != StripeSubscriptionStatus.Map(live.Status))
            return true;

        if (live.CurrentPeriodEnd is { } end
            && (row.CurrentPeriodEnd is null
                || (row.CurrentPeriodEnd.Value.UtcDateTime - DateTime.SpecifyKind(end, DateTimeKind.Utc)).Duration() > PeriodTolerance))
            return true;

        var liveScheduled = live.CancelAtPeriodEnd || live.CancelAt.HasValue;
        var rowScheduled = row.CancelAtPeriodEnd || row.CancelAt.HasValue;
        return liveScheduled != rowScheduled;
    }
}
