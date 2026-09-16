using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Subscription;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Subscription;

namespace Wasnie.Infrastructure.Services;

public sealed class StripeWebhookService(
    IApplicationDbContext db,
    IOptions<StripeOptions> options,
    IOptions<BillingOptions> billingOptions,
    IAssistantPeriodCloser periodCloser,
    IAuditService auditService,
    IClock clock,
    ISubscriptionPlanCatalog catalog,
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
                // KAN-83: a boost is bought with a ONE-OFF checkout, and Stripe announces it with this very same event.
                // Routing on the session's own mode/metadata BEFORE anything else is what keeps the two apart.
                if (IsBoostPurchase(session))
                {
                    var credited = await HandleBoostPurchaseAsync(stripeEvent.Id, session, cancellationToken);
                    if (!credited.IsSuccess)
                    {
                        // ★★ A PAID BOOST THAT COULD NOT BE CREDITED IS NOT ACKNOWLEDGED (§B1). Returning success here
                        // would mark the event processed and the customer's money would buy nothing, silently. Failing
                        // makes Stripe redeliver, and leaves the event visible as failed in its dashboard.
                        return Result<bool>.Failure(credited.Error!);
                    }

                    auditEntry = credited.Value;
                    break;
                }

                auditEntry = await HandleCheckoutSessionCompletedAsync(session, cancellationToken);
                break;

            case EventTypes.CustomerSubscriptionUpdated:
                var updatedSub = stripeEvent.Data.Object as Subscription;
                if (updatedSub is null)
                {
                    logger.LogError("Stripe webhook {EventId}: could not cast data object to Subscription", stripeEvent.Id);
                    return Result<bool>.Failure("Unexpected event payload.");
                }
                auditEntry = await HandleSubscriptionUpdatedAsync(updatedSub, cancellationToken);
                break;

            case EventTypes.CustomerSubscriptionDeleted:
                var deletedSub = stripeEvent.Data.Object as Subscription;
                if (deletedSub is null)
                {
                    logger.LogError("Stripe webhook {EventId}: could not cast data object to Subscription", stripeEvent.Id);
                    return Result<bool>.Failure("Unexpected event payload.");
                }
                auditEntry = await HandleSubscriptionDeletedAsync(deletedSub, cancellationToken);
                break;

            case EventTypes.InvoicePaymentFailed:
                var failedInvoice = stripeEvent.Data.Object as Invoice;
                if (failedInvoice is null)
                {
                    logger.LogError("Stripe webhook {EventId}: could not cast data object to Invoice", stripeEvent.Id);
                    return Result<bool>.Failure("Unexpected event payload.");
                }
                auditEntry = await HandleInvoicePaymentFailedAsync(failedInvoice, cancellationToken);
                break;

            case EventTypes.InvoicePaymentSucceeded:
                var succeededInvoice = stripeEvent.Data.Object as Invoice;
                if (succeededInvoice is null)
                {
                    logger.LogError("Stripe webhook {EventId}: could not cast data object to Invoice", stripeEvent.Id);
                    return Result<bool>.Failure("Unexpected event payload.");
                }
                auditEntry = await HandleInvoicePaymentSucceededAsync(succeededInvoice, cancellationToken);
                break;

            default:
                logger.LogInformation(
                    "Stripe webhook {EventType} acknowledged and ignored (not yet handled)",
                    stripeEvent.Type);
                break;
        }

        // 4. Mark the event as processed — saved atomically with any subscription changes above
        db.ProcessedStripeEvents.Add(ProcessedStripeEvent.Create(stripeEvent.Id, clock.UtcNowOffset));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (!await db.ProcessedStripeEvents.AsNoTracking().AnyAsync(e => e.EventId == stripeEvent.Id, CancellationToken.None))
                throw;

            // ★ Two deliveries of the same event raced past the check above; the other one already saved it (the
            // event id is the key). Nothing of ours was applied twice — this save was rolled back whole.
            logger.LogInformation("Stripe webhook {EventId} was processed concurrently — skipping", stripeEvent.Id);
            return Result<bool>.Success(true);
        }

        // 5. Audit log after the main save (separate transaction — not critical to be atomic)
        if (auditEntry is not null)
            await auditService.LogAsync(auditEntry, cancellationToken);

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Whether this completed checkout bought TOKENS rather than a subscription (KAN-83).
    ///
    /// ★★ THE OLD HANDLER ASSUMED EVERY CHECKOUT WAS A SUBSCRIPTION and asked Stripe for
    /// <c>session.SubscriptionId</c>, which a one-off payment does not have. Without this fork a paid boost threw,
    /// was logged as an error, and the event was still marked processed — the customer paid and got nothing.
    ///
    /// ★ MODE FIRST, METADATA SECOND. The mode is Stripe's own fact about the session; the metadata is ours. Either
    /// one alone would be enough, and requiring both would mean a session created before this tag existed is treated
    /// as a subscription.
    /// </summary>
    private static bool IsBoostPurchase(Session session) =>
        string.Equals(session.Mode, "payment", StringComparison.OrdinalIgnoreCase)
        || (session.Metadata is not null
            && session.Metadata.TryGetValue(StripeBoostService.BoostMetadataKey, out var kind)
            && string.Equals(kind, StripeBoostService.BoostMetadataValue, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Credits a bought boost to the tenant's balance (KAN-83).
    ///
    /// ★ THE AMOUNT COMES FROM THE PRODUCT, not from our configuration and not from the price id. Resizing a pack in
    /// Stripe then needs no deploy, and the row keeps what was actually granted.
    ///
    /// ★ THE STRIPE EVENT ID IS THE IDEMPOTENCY KEY, enforced by a unique index. The dedup check at the top of
    /// ProcessAsync already stops an ordinary redelivery; the index is what stops two deliveries racing past it.
    /// </summary>
    private async Task<Result<AuditEntry?>> HandleBoostPurchaseAsync(
        string eventId, Session session, CancellationToken cancellationToken)
    {
        if (session.Metadata is null
            || !session.Metadata.TryGetValue("tenantId", out var tenantIdRaw)
            || !Guid.TryParse(tenantIdRaw, out var tenantId))
        {
            logger.LogError(
                "Boost checkout {SessionId} carries no usable tenantId metadata; nothing credited", session.Id);
            return Result<AuditEntry?>.Failure("Boost checkout has no tenant.");
        }

        var client = new StripeClient(options.Value.SecretKey);

        // The webhook payload does not carry line items; the product (and its token metadata) has to be fetched.
        Session full;
        try
        {
            full = await new SessionService(client).GetAsync(
                session.Id,
                new SessionGetOptions { Expand = ["line_items.data.price.product"] },
                cancellationToken: cancellationToken);
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Could not read boost checkout {SessionId} back from Stripe", session.Id);
            return Result<AuditEntry?>.Failure("Boost checkout could not be read.");
        }

        var line = full.LineItems?.Data?.FirstOrDefault();
        if (line?.Price?.Product is not Product product)
        {
            logger.LogError("Boost checkout {SessionId} has no product on its first line item", session.Id);
            return Result<AuditEntry?>.Failure("Boost checkout has no product.");
        }

        if (!StripeBoostService.TryReadTokens(product, out var tokens))
        {
            logger.LogError(
                "Boost product {ProductId} has no usable '{Key}' metadata; refusing to credit an invented amount for {SessionId}",
                product.Id, AssistantBoostOptions.TokensMetadataKey, session.Id);
            return Result<AuditEntry?>.Failure("Boost product does not say how many tokens it grants.");
        }

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant is null)
        {
            logger.LogError("Tenant {TenantId} not found for boost checkout {SessionId}", tenantId, session.Id);
            return Result<AuditEntry?>.Failure("Tenant not found.");
        }

        var now = clock.UtcNowOffset;

        db.AssistantTokenBoosts.Add(Wasnie.Domain.Assistant.AssistantTokenBoost.Create(
            id: Guid.NewGuid(),
            tenantId: tenantId,
            stripeEventId: eventId,
            stripeProductId: product.Id,
            stripeSessionId: session.Id,
            tokens: tokens,
            purchasedAt: now,
            // Frozen per lot: changing the setting later must never shorten a boost already sold.
            expiresAt: now.AddDays(billingOptions.Value.Boosts.ExpiryDays)));

        logger.LogInformation(
            "Tenant {TenantId} credited a boost of {Tokens} tokens from product {ProductId} (session {SessionId})",
            tenantId, tokens, product.Id, session.Id);

        return Result<AuditEntry?>.Success(new AuditEntry(
            TenantId: tenantId,
            Action: AuditActions.AssistantBoostPurchased,
            ResourceType: ResourceTypes.Subscription,
            ResourceId: session.Id,
            ActorUserId: "stripe-webhook",
            ActorEmail: "webhook@stripe.com",
            DisplayName: $"Assistant boost purchased: {tokens} tokens ({product.Id})"));
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

        // Resolve the plan from the product (KAN-77: the plan catalog, not the old tier enum)
        var item = stripeSubscription.Items?.Data?.FirstOrDefault();
        if (item?.Price?.Product is not Product product)
        {
            logger.LogError(
                "Stripe subscription {SubscriptionId} has no product on its first item",
                stripeSubscription.Id);
            return null;
        }

        var plan = catalog.ResolveStripeProduct(product.Id, product.Metadata);
        if (plan is null)
        {
            logger.LogError(
                "Could not resolve a plan for product {ProductId} in session {SessionId}",
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
            subscription = UserSubscription.CreatePending(
                id: Guid.NewGuid(),
                tenantId: tenantId,
                billingEmail: session.CustomerEmail ?? string.Empty,
                now: now);
            db.UserSubscriptions.Add(subscription);
        }

        subscription.UpdateFromStripe(
            planCode: plan.Code,
            status: SubscriptionStatus.Active,
            stripeSubscriptionId: stripeSubscription.Id,
            stripeCustomerId: stripeSubscription.CustomerId,
            stripePriceId: item.Price.Id,
            stripeProductId: product.Id,
            periodStart: periodStart,
            periodEnd: periodEnd,
            nextBillingDate: periodEnd,
            now: now);


        tenant.SelectPlan(plan.Code);

        logger.LogInformation(
            "Tenant {TenantId} subscription activated: plan={Plan} subscription={SubscriptionId}",
            tenantId, plan.Code, stripeSubscription.Id);

        return new AuditEntry(
            TenantId: tenantId,
            Action: AuditActions.SubscriptionActivated,
            ResourceType: ResourceTypes.Subscription,
            ResourceId: stripeSubscription.Id,
            ActorUserId: "stripe-webhook",
            ActorEmail: "webhook@stripe.com",
            DisplayName: $"Subscription activated: {plan.Code} via Stripe session {session.Id}");
    }

    private async Task<AuditEntry?> HandleSubscriptionUpdatedAsync(
        Subscription stripeSubscription,
        CancellationToken cancellationToken)
    {
        var client = new StripeClient(options.Value.SecretKey);
        var subscriptionService = new SubscriptionService(client);

        Subscription fullSubscription;
        try
        {
            fullSubscription = await subscriptionService.GetAsync(
                stripeSubscription.Id,
                new SubscriptionGetOptions { Expand = ["items.data.price.product"] },
                cancellationToken: cancellationToken);
        }
        catch (StripeException ex)
        {
            logger.LogError(ex,
                "Failed to fetch full subscription {SubscriptionId} for subscription.updated webhook",
                stripeSubscription.Id);
            return null;
        }

        var item = fullSubscription.Items?.Data?.FirstOrDefault();
        if (item?.Price?.Product is not Product product)
        {
            logger.LogError(
                "subscription.updated: no product on first item of subscription {SubscriptionId}",
                fullSubscription.Id);
            return null;
        }

        var newPlan = catalog.ResolveStripeProduct(product.Id, product.Metadata);
        if (newPlan is null)
        {
            logger.LogError(
                "subscription.updated: could not resolve a plan for product {ProductId}",
                product.Id);
            return null;
        }

        var subscription = await db.UserSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == fullSubscription.Id, cancellationToken);

        if (subscription is null)
        {
            logger.LogWarning(
                "subscription.updated: no UserSubscription found for StripeSubscriptionId {SubscriptionId}",
                fullSubscription.Id);
            return null;
        }

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == subscription.TenantId, cancellationToken);

        if (tenant is null)
        {
            logger.LogError(
                "subscription.updated: tenant {TenantId} not found",
                subscription.TenantId);
            return null;
        }

        return await StripeSubscriptionApplier.ApplyUpdate(
            subscription, tenant, fullSubscription, stripeSubscription, item, product, newPlan, catalog,
            clock.UtcNowOffset, StripeChangeActor.Webhook, logger, periodCloser, cancellationToken);
    }

    private async Task<AuditEntry?> HandleSubscriptionDeletedAsync(
        Subscription stripeSubscription,
        CancellationToken cancellationToken)
    {
        // ★★ BY SUBSCRIPTION ID, NOT BY CUSTOMER (KAN-77, runtime). One customer can own an old cancelled
        // subscription and a new paid one. Matched by customer, the old one's deletion — delivered late, retried or
        // resent — cancelled the row that already follows the NEW subscription, and locked out a customer who had
        // just paid. A deletion only ends the subscription the row actually holds.
        var subscription = await db.UserSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscription.Id, cancellationToken);

        if (subscription is null)
        {
            logger.LogWarning(
                "subscription.deleted: no UserSubscription holds {SubscriptionId} (customer {CustomerId}); ignored",
                stripeSubscription.Id, stripeSubscription.CustomerId);
            return null;
        }

        var endedAt = stripeSubscription.EndedAt ?? stripeSubscription.CanceledAt;
        subscription.Cancel(clock.UtcNowOffset, endedAt.HasValue ? new DateTimeOffset(endedAt.Value, TimeSpan.Zero) : null);

        logger.LogInformation(
            "Tenant {TenantId} subscription canceled via webhook",
            subscription.TenantId);

        return new AuditEntry(
            TenantId: subscription.TenantId,
            Action: AuditActions.SubscriptionCanceled,
            ResourceType: ResourceTypes.Subscription,
            ResourceId: stripeSubscription.Id,
            ActorUserId: "stripe-webhook",
            ActorEmail: "webhook@stripe.com",
            DisplayName: $"Subscription canceled by Stripe: {stripeSubscription.Id}");
    }

    private async Task<AuditEntry?> HandleInvoicePaymentFailedAsync(
        Invoice invoice,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(invoice.CustomerId))
        {
            logger.LogWarning("invoice.payment_failed: invoice {InvoiceId} has no CustomerId", invoice.Id);
            return null;
        }

        var subscription = await FindInvoiceSubscriptionAsync(invoice, cancellationToken);

        if (subscription is null)
        {
            logger.LogWarning(
                "invoice.payment_failed: no UserSubscription holds the invoice's subscription (customer {CustomerId})",
                invoice.CustomerId);
            return null;
        }

        subscription.MarkPastDue(clock.UtcNowOffset);

        logger.LogInformation(
            "Tenant {TenantId} subscription marked PastDue due to failed payment",
            subscription.TenantId);

        return new AuditEntry(
            TenantId: subscription.TenantId,
            Action: AuditActions.SubscriptionPastDue,
            ResourceType: ResourceTypes.Subscription,
            ResourceId: invoice.Id,
            ActorUserId: "stripe-webhook",
            ActorEmail: "webhook@stripe.com",
            DisplayName: $"Payment failed for invoice {invoice.Id} — subscription marked PastDue");
    }

    private async Task<AuditEntry?> HandleInvoicePaymentSucceededAsync(
        Invoice invoice,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(invoice.CustomerId))
            return null;

        var subscription = await FindInvoiceSubscriptionAsync(invoice, cancellationToken);

        if (subscription is null)
            return null;

        // Only recover if currently PastDue — idempotent for all other states.
        if (subscription.Status != SubscriptionStatus.PastDue)
            return null;

        subscription.Recover(clock.UtcNowOffset);

        logger.LogInformation(
            "Tenant {TenantId} subscription recovered from PastDue after successful payment",
            subscription.TenantId);

        return new AuditEntry(
            TenantId: subscription.TenantId,
            Action: AuditActions.SubscriptionRecovered,
            ResourceType: ResourceTypes.Subscription,
            ResourceId: invoice.Id,
            ActorUserId: "stripe-webhook",
            ActorEmail: "webhook@stripe.com",
            DisplayName: $"Payment succeeded for invoice {invoice.Id} — subscription recovered");
    }

    /// <summary>
    /// The row an invoice belongs to. ★ When the invoice names its subscription, the row must hold THAT subscription:
    /// a failed invoice of a customer's old subscription must not mark their new one PastDue. Invoices that do not name
    /// one (older payload shapes) fall back to the customer, as before.
    /// </summary>
    private async Task<UserSubscription?> FindInvoiceSubscriptionAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var subscriptionId = invoice.Parent?.SubscriptionDetails?.SubscriptionId;

        return string.IsNullOrEmpty(subscriptionId)
            ? await db.UserSubscriptions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.StripeCustomerId == invoice.CustomerId, cancellationToken)
            : await db.UserSubscriptions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.StripeSubscriptionId == subscriptionId, cancellationToken);
    }
}
