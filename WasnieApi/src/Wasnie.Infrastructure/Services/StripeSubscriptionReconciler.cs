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
            // Not something to guess about: a row pointing at a subscription Stripe never had is reported, not cancelled.
            logger.LogWarning(
                "Subscription sync: Stripe has no subscription {SubscriptionId} for tenant {TenantId}; row left untouched",
                targetId, tenantId);
            return false;
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
            var specific = StripeSubscriptionApplier.ApplyUpdate(
                subscription, tenant, full, full, item, product, plan, catalog, now, StripeChangeActor.Sync, logger);

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
