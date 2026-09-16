using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Subscription;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Subscription;
using StripeSubscription = Stripe.Subscription;

namespace Wasnie.Infrastructure.Services;

/// <summary>
/// The safety net for webhooks that never arrived (KAN-77). Asks Stripe which subscription this tenant REALLY has and
/// makes the stored row say the same, with the webhook's own rules (<see cref="StripeSubscriptionApplier"/>).
///
/// ★★ THE LIVE SUBSCRIPTION WINS, WHATEVER ID THE ROW HOLDS. Runtime, 14-sep: the customer's old subscription was
/// cancelled, they checked out again and paid €299 — a NEW subscription id — and no webhook reached us. The first
/// version of this class only re-read the id already stored, found it cancelled and left a paying customer locked
/// out. It now looks at every subscription of the customer AND at subscriptions tagged with the tenant (a first
/// checkout creates a new customer the row has never heard of), and adopts the most recent live one.
///
/// ★ IT NEVER CHARGES AND NEVER WRITES TO STRIPE. Reads there; writes only our database.
///
/// ★ EVERY CORRECTION IS AUDITED, even a renewal-only one the webhook would not record: a sync writing state nobody
/// asked for must leave a row saying so. An ended subscription is cancelled with the date Stripe ended it — not the
/// date we happened to notice (§B6).
/// </summary>
public sealed class StripeSubscriptionReconciler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    IOptions<StripeOptions> options,
    IAuditService auditService,
    IClock clock,
    ISubscriptionPlanCatalog catalog,
    IAssistantPeriodCloser periodCloser,
    ILogger<StripeSubscriptionReconciler> logger)
    : IStripeSubscriptionReconciler
{
    public async Task<bool> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        if (!tenantContext.IsResolved)
            return false;

        var tenantId = tenantContext.TenantId;
        var subscription = await db.UserSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        var service = new SubscriptionService(new StripeClient(options.Value.SecretKey));

        var live = await FindLiveSubscriptionIdAsync(service, tenantId, subscription?.StripeCustomerId, cancellationToken);
        var targetId = live ?? subscription?.StripeSubscriptionId;
        if (string.IsNullOrEmpty(targetId))
            return false;

        StripeSubscription full;
        try
        {
            full = await service.GetAsync(
                targetId,
                new SubscriptionGetOptions { Expand = ["items.data.price.product", "customer"] },
                cancellationToken: cancellationToken);
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
        {
            // ★★ THE ROW POINTS AT A SUBSCRIPTION STRIPE DOES NOT HAVE. Found in the wild: a tenant sat in PastDue
            // since July with full access, on a subscription that had ceased to exist — the webhook was missed and
            // nothing ever asked Stripe again.
            //
            // ★ CANCELLED ONLY ON CERTAINTY, NEVER ON A 404 ALONE. `resource_missing` also fires when the key points
            // at a different Stripe account or environment, and treating that as "no subscription" would lock out
            // EVERY paying customer at once. So the row is only closed when Stripe answers a SECOND, independent
            // question — does this customer hold any live subscription? — and says no. Anything else (no customer on
            // file, Stripe unreachable, the call failing) leaves the row untouched and logs, exactly as before:
            // a doubt must never cost a paying customer their access.
            if (subscription is null || subscription.Status == SubscriptionStatus.Canceled
                || !await ConfirmCustomerHasNoLiveSubscriptionAsync(service, subscription.StripeCustomerId, cancellationToken))
            {
                logger.LogWarning(
                    "Subscription sync: Stripe has no subscription {SubscriptionId} for tenant {TenantId} and it could "
                    + "not be confirmed that the customer holds none; row left untouched",
                    targetId, tenantId);
                return false;
            }

            var missingFrom = subscription.Status;
            subscription.Cancel(clock.UtcNowOffset, null);

            logger.LogWarning(
                "Subscription sync: tenant {TenantId} was {Status} on subscription {SubscriptionId}, which Stripe does "
                + "not have, and the customer holds no live subscription; cancelled",
                tenantId, missingFrom, targetId);

            await auditService.LogAsync(
                Audit(tenantId, AuditActions.SubscriptionCanceled, targetId,
                    $"Subscription canceled: Stripe has no {targetId} and the customer holds none (synced)"),
                cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var now = clock.UtcNowOffset;
        AuditEntry audit;

        if (StripeSubscriptionStatus.IsEnded(full.Status))
        {
            // Only reached with the stored id (a live one was not found): nothing better exists in Stripe.
            if (subscription is null || subscription.Status == SubscriptionStatus.Canceled)
                return false;

            var previousStatus = subscription.Status;
            var endedAt = full.EndedAt ?? full.CanceledAt;
            subscription.Cancel(now, endedAt.HasValue ? new DateTimeOffset(endedAt.Value, TimeSpan.Zero) : null);

            logger.LogWarning(
                "Subscription sync: tenant {TenantId} was {Status} locally but Stripe ended {SubscriptionId} at {EndedAt}; cancelled",
                tenantId, previousStatus, full.Id, endedAt);

            audit = Audit(tenantId, AuditActions.SubscriptionCanceled, full.Id,
                $"Subscription canceled by Stripe (synced, webhook missed): {full.Id}");
        }
        else
        {
            var item = full.Items?.Data?.FirstOrDefault();
            if (item?.Price?.Product is not Product product)
            {
                logger.LogError("Subscription sync: no product on first item of subscription {SubscriptionId}", full.Id);
                return false;
            }

            var plan = catalog.ResolveStripeProduct(product.Id, product.Metadata);
            if (plan is null)
            {
                logger.LogError("Subscription sync: could not resolve a plan for product {ProductId}", product.Id);
                return false;
            }

            var switchedSubscription = subscription?.StripeSubscriptionId != full.Id;
            if (!switchedSubscription && !StripeSubscriptionDrift.Differs(subscription!, Snapshot(full, item)))
                return false;

            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
            if (tenant is null)
            {
                logger.LogError("Subscription sync: tenant {TenantId} not found", tenantId);
                return false;
            }

            if (subscription is null)
            {
                subscription = UserSubscription.CreatePending(Guid.NewGuid(), tenantId, full.Customer?.Email ?? string.Empty, now);
                db.UserSubscriptions.Add(subscription);
            }

            var previousId = subscription.StripeSubscriptionId;
            var specific = await StripeSubscriptionApplier.ApplyUpdate(
                subscription, tenant, full, full, item, product, plan, catalog, now, StripeChangeActor.Sync, logger,
                periodCloser, cancellationToken);

            logger.LogWarning(
                "Subscription sync: tenant {TenantId} now follows Stripe subscription {SubscriptionId} ({Status}); was {PreviousId} (webhook missed)",
                tenantId, full.Id, full.Status, previousId);

            audit = switchedSubscription
                ? Audit(tenantId, AuditActions.SubscriptionActivated, full.Id,
                    $"Subscription activated from Stripe (synced, webhook missed): {plan.Code}, replaces {previousId ?? "none"}")
                : specific ?? Audit(tenantId, AuditActions.SubscriptionSyncedFromStripe, full.Id,
                    $"Subscription synced from Stripe (webhook missed): {full.Status}, period ends {item.CurrentPeriodEnd:O}");
        }

        await db.SaveChangesAsync(cancellationToken);
        await auditService.LogAsync(audit, cancellationToken);
        return true;
    }

    /// <summary>
    /// The most recent subscription that gives access, among the customer's subscriptions and those tagged with the
    /// tenant at checkout. Search is best-effort (Stripe indexes it with a delay); the customer list is exact.
    /// </summary>
    /// <summary>
    /// Whether Stripe CONFIRMS this customer holds no live subscription.
    ///
    /// ★ FALSE MEANS "COULD NOT CONFIRM", NOT "HAS ONE". No customer on file, a failed call, a network error —
    /// all return false, because the only safe default when closing somebody's access is to do nothing.
    /// </summary>
    private async Task<bool> ConfirmCustomerHasNoLiveSubscriptionAsync(
        SubscriptionService service, string? customerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(customerId))
            return false;

        try
        {
            var all = await service.ListAsync(
                new SubscriptionListOptions { Customer = customerId, Status = "all", Limit = 20 },
                cancellationToken: cancellationToken);

            return all.Data.All(s => StripeSubscriptionStatus.IsEnded(s.Status));
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Subscription sync: could not list subscriptions for customer {CustomerId}", customerId);
            return false;
        }
    }

    private async Task<string?> FindLiveSubscriptionIdAsync(
        SubscriptionService service, Guid tenantId, string? customerId, CancellationToken cancellationToken)
    {
        var candidates = new List<StripeSubscription>();

        if (!string.IsNullOrEmpty(customerId))
        {
            var byCustomer = await service.ListAsync(
                new SubscriptionListOptions { Customer = customerId, Status = "all", Limit = 20 },
                cancellationToken: cancellationToken);
            candidates.AddRange(byCustomer.Data);
        }

        try
        {
            var byTenant = await service.SearchAsync(
                new SubscriptionSearchOptions { Query = $"metadata['tenantId']:'{tenantId}'", Limit = 20 },
                cancellationToken: cancellationToken);
            candidates.AddRange(byTenant.Data);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Subscription sync: search by tenant {TenantId} failed; using the customer list only", tenantId);
        }

        return StripeSubscriptionStatus.PickLive(candidates.Select(s => (s.Id, s.Status, s.Created)));
    }

    private static StripeSubscriptionSnapshot Snapshot(StripeSubscription s, SubscriptionItem item) => new(
        s.Status, item.CurrentPeriodStart, item.CurrentPeriodEnd, s.CancelAtPeriodEnd, s.CancelAt, s.EndedAt);

    private static AuditEntry Audit(Guid tenantId, string action, string subscriptionId, string displayName) => new(
        TenantId: tenantId,
        Action: action,
        ResourceType: ResourceTypes.Subscription,
        ResourceId: subscriptionId,
        ActorUserId: StripeChangeActor.Sync.UserId,
        ActorEmail: StripeChangeActor.Sync.Email,
        DisplayName: displayName);
}
