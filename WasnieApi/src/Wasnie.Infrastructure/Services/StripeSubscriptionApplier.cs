using Microsoft.Extensions.Logging;
using Stripe;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Subscription;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Entities;
using Wasnie.Domain.Subscription;

namespace Wasnie.Infrastructure.Services;

/// <summary>Who changed the subscription row, for the audit trail.</summary>
internal sealed record StripeChangeActor(string UserId, string Email, string Source)
{
    public static readonly StripeChangeActor Webhook = new("stripe-webhook", "webhook@stripe.com", "subscription.updated");
    public static readonly StripeChangeActor Sync = new("stripe-sync", "sync@stripe.com", "subscription sync");
}

/// <summary>
/// ★ ONE SET OF RULES FOR "STRIPE SAYS THE SUBSCRIPTION IS NOW X". Moved out of the <c>subscription.updated</c>
/// webhook so the on-read sync (KAN-77) applies exactly the same status mapping, period, plan and cancellation
/// schedule — a second copy would drift from the first the day somebody fixed only one of them.
/// </summary>
internal static class StripeSubscriptionApplier
{
    /// <param name="cancelSource">
    /// Where the cancellation flags are read from. The webhook keeps reading them from the EVENT payload, as it always
    /// has; the sync reads them from the subscription it just fetched.
    /// </param>
    public static async Task<AuditEntry?> ApplyUpdate(
        UserSubscription subscription,
        Tenant tenant,
        Subscription fullSubscription,
        Subscription cancelSource,
        SubscriptionItem item,
        Product product,
        SubscriptionPlanDefinition newPlan,
        ISubscriptionPlanCatalog catalog,
        DateTimeOffset now,
        StripeChangeActor actor,
        ILogger logger,
        IAssistantPeriodCloser periodCloser,
        CancellationToken cancellationToken)
    {
        var periodStart = new DateTimeOffset(item.CurrentPeriodStart, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(item.CurrentPeriodEnd, TimeSpan.Zero);
        var previousPlanCode = subscription.PlanCode;
        var wasCancelScheduled = subscription.CancelAtPeriodEnd;

        // ★★ KAN-83: BEFORE the period moves. The included allowance belongs to a period, and the moment the
        // row below is updated the old window's boundaries are gone — with them, any way to know what that period spent
        // past its allowance. Placed here rather than at the two call sites because this is the one road both take.
        await periodCloser.CloseIfRolledOverAsync(
            subscription.TenantId, subscription.CurrentPeriodStart, subscription.CurrentPeriodEnd,
            periodStart, cancellationToken);

        var mappedStatus = StripeSubscriptionStatus.Map(fullSubscription.Status);

        subscription.UpdateFromStripe(
            planCode: newPlan.Code,
            status: mappedStatus,
            stripeSubscriptionId: fullSubscription.Id,
            stripeCustomerId: fullSubscription.CustomerId,
            stripePriceId: item.Price.Id,
            stripeProductId: product.Id,
            periodStart: periodStart,
            periodEnd: periodEnd,
            nextBillingDate: periodEnd,
            now: now);

        tenant.SelectPlan(newPlan.Code);

        // Log cancellation signal values for diagnosis (flexible billing uses cancel_at, not cancel_at_period_end).
        logger.LogInformation(
            "{Source}: CancelAtPeriodEnd={CancelAtPeriodEnd} CancelAt={CancelAt} for {SubscriptionId}",
            actor.Source, cancelSource.CancelAtPeriodEnd, cancelSource.CancelAt, cancelSource.Id);

        // Classic mode: cancel_at_period_end=true. Flexible (billing_mode=flexible / dahlia): cancel_at != null, cancel_at_period_end stays false.
        var isCancelScheduled = cancelSource.CancelAtPeriodEnd || cancelSource.CancelAt.HasValue;
        if (isCancelScheduled)
        {
            var cancelAt = cancelSource.CancelAt.HasValue
                ? new DateTimeOffset(cancelSource.CancelAt.Value, TimeSpan.Zero)
                : periodEnd;

            subscription.ScheduleCancellation(cancelAt, now);

            logger.LogInformation(
                "Tenant {TenantId} subscription scheduled for cancellation at {CancelAt}",
                subscription.TenantId, cancelAt);

            return new AuditEntry(
                TenantId: subscription.TenantId,
                Action: AuditActions.SubscriptionCancelScheduled,
                ResourceType: ResourceTypes.Subscription,
                ResourceId: fullSubscription.Id,
                ActorUserId: actor.UserId,
                ActorEmail: actor.Email,
                DisplayName: $"Subscription cancel scheduled at {cancelAt:O}: {fullSubscription.Id}");
        }

        if (wasCancelScheduled)
        {
            subscription.ClearCancellationSchedule(now);

            logger.LogInformation(
                "Tenant {TenantId} subscription cancellation reverted",
                subscription.TenantId);

            return new AuditEntry(
                TenantId: subscription.TenantId,
                Action: AuditActions.SubscriptionCancelReverted,
                ResourceType: ResourceTypes.Subscription,
                ResourceId: fullSubscription.Id,
                ActorUserId: actor.UserId,
                ActorEmail: actor.Email,
                DisplayName: $"Subscription cancellation reverted: {fullSubscription.Id}");
        }

        // ★ No plan change (a renewal, a status or period update): nothing to record as an upgrade or downgrade.
        // Before KAN-77 an update between two equal tiers was logged as a DOWNGRADE, because the comparison was
        // "new > previous" and anything else fell into the other branch.
        if (string.Equals(previousPlanCode, newPlan.Code, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Tenant {TenantId} subscription updated without a plan change ({Plan}, {Status})",
                subscription.TenantId, newPlan.Code, mappedStatus);
            return null;
        }

        // Direction by what the plan allows: more room is an upgrade. Unlimited counts as the most room.
        var isUpgrade = catalog.IsUpgrade(catalog.Find(previousPlanCode), newPlan);

        logger.LogInformation(
            "Tenant {TenantId} subscription plan changed: {Previous} → {New}",
            subscription.TenantId, previousPlanCode, newPlan.Code);

        return new AuditEntry(
            TenantId: subscription.TenantId,
            Action: isUpgrade ? AuditActions.SubscriptionUpgraded : AuditActions.SubscriptionDowngraded,
            ResourceType: ResourceTypes.Subscription,
            ResourceId: fullSubscription.Id,
            ActorUserId: actor.UserId,
            ActorEmail: actor.Email,
            DisplayName: $"Subscription {(isUpgrade ? "upgraded" : "downgraded")}: {previousPlanCode} → {newPlan.Code}");
    }
}
