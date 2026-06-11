using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Subscription;

namespace Wasnie.Infrastructure.Services;

public sealed class StripeWebhookService(
    IApplicationDbContext db,
    IOptions<StripeOptions> options,
    IAuditService auditService,
    IClock clock,
    ILogger<StripeWebhookService> logger)
    : IStripeWebhookService
{
    public async Task<Result<bool>> ProcessAsync(
        string json,
        string stripeSignature,
        CancellationToken cancellationToken = default)
    {
        // 1. Verify Stripe webhook signature — reject anything that can't be verified
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                json,
                stripeSignature,
                options.Value.WebhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe webhook signature verification failed");
            return Result<bool>.Failure("Invalid webhook signature.");
        }

        logger.LogInformation(
            "Stripe webhook received: {EventType} {EventId}",
            stripeEvent.Type, stripeEvent.Id);

        // 2. Idempotency check (M2) — deduplicate by Stripe event ID
        var alreadyProcessed = await db.ProcessedStripeEvents
            .AnyAsync(e => e.EventId == stripeEvent.Id, cancellationToken);

        if (alreadyProcessed)
        {
            logger.LogInformation(
                "Stripe webhook {EventId} already processed — skipping",
                stripeEvent.Id);
            return Result<bool>.Success(true);
        }

        // 3. Dispatch to event-specific handler (returns audit entry if applicable)
        AuditEntry? auditEntry = null;
        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
                var session = stripeEvent.Data.Object as Session;
                if (session is null)
                {
                    logger.LogError("Stripe webhook {EventId}: could not cast data object to Session", stripeEvent.Id);
                    return Result<bool>.Failure("Unexpected event payload.");
                }
                auditEntry = await HandleCheckoutSessionCompletedAsync(session, cancellationToken);
                break;

            default:
                logger.LogInformation(
                    "Stripe webhook {EventType} acknowledged and ignored (not yet handled)",
                    stripeEvent.Type);
                break;
        }

        // 4. Mark the event as processed — saved atomically with any subscription changes above
        db.ProcessedStripeEvents.Add(ProcessedStripeEvent.Create(stripeEvent.Id, clock.UtcNowOffset));
        await db.SaveChangesAsync(cancellationToken);

        // 5. Audit log after the main save (separate transaction — not critical to be atomic)
        if (auditEntry is not null)
            await auditService.LogAsync(auditEntry, cancellationToken);

        return Result<bool>.Success(true);
    }

    // Returns an AuditEntry to be persisted after the main save, or null if no audit is needed.
    private async Task<AuditEntry?> HandleCheckoutSessionCompletedAsync(
        Session session,
        CancellationToken cancellationToken)
    {
        // Extract TenantId from server-side metadata (set by our backend when creating the session)
        if (!session.Metadata.TryGetValue("tenantId", out var tenantIdRaw)
            || !Guid.TryParse(tenantIdRaw, out var tenantId))
        {
            logger.LogError(
                "Stripe checkout.session.completed missing or invalid tenantId metadata. SessionId={SessionId}",
                session.Id);
            return null;
        }

        // Retrieve the full Stripe Subscription to get period/price/product details
        var client = new StripeClient(options.Value.SecretKey);
        var stripeSubscriptionService = new SubscriptionService(client);

        Subscription stripeSubscription;
        try
        {
            stripeSubscription = await stripeSubscriptionService.GetAsync(
                session.SubscriptionId,
                new SubscriptionGetOptions { Expand = ["items.data.price.product"] },
                cancellationToken: cancellationToken);
        }
        catch (StripeException ex)
        {
            logger.LogError(ex,
                "Failed to retrieve Stripe subscription {SubscriptionId} for session {SessionId}",
                session.SubscriptionId, session.Id);
            return null;
        }

        // Resolve the Wasnie tier from the product
        var item = stripeSubscription.Items?.Data?.FirstOrDefault();
        if (item?.Price?.Product is not Product product)
        {
            logger.LogError(
                "Stripe subscription {SubscriptionId} has no product on its first item",
                stripeSubscription.Id);
            return null;
        }

        var tierSlug = StripeSubscriptionPlanService.ResolveTier(
            product.Id, product.Metadata, options.Value.ProductTierMap, logger);

        if (tierSlug is null || !Enum.TryParse<Tier>(tierSlug, ignoreCase: true, out var tier) || tier == Tier.Free)
        {
            logger.LogError(
                "Could not resolve a valid paid tier for product {ProductId} in session {SessionId}",
                product.Id, session.Id);
            return null;
        }

        // Find the tenant (bypass global query filter — no JWT in webhook context)
        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant is null)
        {
            logger.LogError("Tenant {TenantId} not found for Stripe session {SessionId}", tenantId, session.Id);
            return null;
        }

        var now = clock.UtcNowOffset;
        // CurrentPeriodStart/End live on SubscriptionItem in Stripe.net v52+
        var periodStart = new DateTimeOffset(item.CurrentPeriodStart, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(item.CurrentPeriodEnd, TimeSpan.Zero);

        // Upsert UserSubscription
        var subscription = await db.UserSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

        if (subscription is null)
        {
            subscription = UserSubscription.CreateFree(
                id: Guid.NewGuid(),
                tenantId: tenantId,
                billingEmail: session.CustomerEmail ?? string.Empty,
                now: now);
            db.UserSubscriptions.Add(subscription);
        }

        subscription.UpdateFromStripe(
            tier: tier,
            status: SubscriptionStatus.Active,
            stripeSubscriptionId: stripeSubscription.Id,
            stripeCustomerId: stripeSubscription.CustomerId,
            stripePriceId: item.Price.Id,
            stripeProductId: product.Id,
            periodStart: periodStart,
            periodEnd: periodEnd,
            nextBillingDate: periodEnd,
            now: now);


        tenant.SelectPlan(tier);

        logger.LogInformation(
            "Tenant {TenantId} subscription activated: tier={Tier} subscription={SubscriptionId}",
            tenantId, tier, stripeSubscription.Id);

        return new AuditEntry(
            TenantId: tenantId,
            Action: AuditActions.SubscriptionActivated,
            ResourceType: ResourceTypes.Subscription,
            ResourceId: stripeSubscription.Id,
            ActorUserId: "stripe-webhook",
            ActorEmail: "webhook@stripe.com",
            DisplayName: $"Subscription activated: {tier} via Stripe session {session.Id}");
    }
}
