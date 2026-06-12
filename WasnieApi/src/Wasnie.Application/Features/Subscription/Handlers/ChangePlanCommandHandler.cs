using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription.Commands;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Subscription.Handlers;

public sealed class ChangePlanCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ISubscriptionPlanService planService,
    IStripeSubscriptionManagementService stripeManagement,
    ILogger<ChangePlanCommandHandler> logger,
    IClock clock,
    IAuditService audit)
    : IRequestHandler<ChangePlanCommand, Result<ChangePlanResultDto>>
{
    public async Task<Result<ChangePlanResultDto>> Handle(
        ChangePlanCommand request,
        CancellationToken cancellationToken)
    {
        // Only paid tiers (Starter/Growth/Scale) are valid targets.
        if (!Enum.TryParse<Tier>(request.TargetTier, ignoreCase: true, out var targetTier)
            || targetTier is Tier.Free or Tier.Enterprise)
        {
            return Result<ChangePlanResultDto>.Failure(
                $"'{request.TargetTier}' is not a valid upgrade target. Use Starter, Growth, or Scale.");
        }

        var tenantId = tenantContext.TenantId;

        var subscription = await db.UserSubscriptions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

        if (subscription is null)
            return Result<ChangePlanResultDto>.Failure("No subscription found.");

        if (string.IsNullOrEmpty(subscription.StripeSubscriptionId))
            return Result<ChangePlanResultDto>.Failure("No active paid Stripe subscription to change.");

        // ── Read authoritative tier from Stripe ───────────────────────────────
        // The DB tier can be stale: it only updates when customer.subscription.updated
        // webhook arrives (seconds to minutes after a prior change). Trusting the DB here
        // would route the wrong branch (upgrade treated as downgrade or vice versa) and
        // either miss a charge or add an unwanted proration. Stripe is always authoritative.
        Tier stripeCurrentTier;
        try
        {
            var tierFromStripe = await stripeManagement.GetCurrentTierFromStripeAsync(
                subscription.StripeSubscriptionId!, cancellationToken);

            if (tierFromStripe is null)
            {
                logger.LogError(
                    "Stripe subscription {SubId} for tenant {TenantId} has no mappable tier — aborting plan change",
                    subscription.StripeSubscriptionId, tenantId);
                return Result<ChangePlanResultDto>.Failure("plan_change_unavailable");
            }

            stripeCurrentTier = tierFromStripe.Value;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to read current tier from Stripe for subscription {SubId} (tenant {TenantId})",
                subscription.StripeSubscriptionId, tenantId);
            return Result<ChangePlanResultDto>.Failure("plan_change_unavailable");
        }

        // ── Sync DB if stale ──────────────────────────────────────────────────
        // If the DB tier diverged from Stripe (e.g. a prior change whose webhook has not
        // arrived yet), correct it now so future reads are accurate.
        if (subscription.Tier != stripeCurrentTier)
        {
            var staleTier = subscription.Tier;
            subscription.SyncTier(stripeCurrentTier, clock.UtcNowOffset);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Synced stale DB tier for tenant {TenantId}: {OldTier} → {NewTier} (source: Stripe)",
                tenantId, staleTier, stripeCurrentTier);

            await audit.LogAsync(new AuditEntry(
                TenantId:     tenantId,
                Action:       AuditActions.SubscriptionTierSyncedFromStripe,
                ResourceType: "UserSubscription",
                ResourceId:   subscription.Id.ToString(),
                ActorUserId:  "SYSTEM",
                ActorEmail:   "system@wasnie.io",
                DisplayName:  subscription.BillingEmail,
                Before:       new { Tier = staleTier.ToString() },
                After:        new { Tier = stripeCurrentTier.ToString() },
                Metadata:     new Dictionary<string, string>
                {
                    ["stripeSubscriptionId"] = subscription.StripeSubscriptionId!,
                    ["trigger"]             = "ChangePlanCommand",
                }), cancellationToken);
        }

        // ── Same-tier guard ───────────────────────────────────────────────────
        if (stripeCurrentTier == targetTier)
            return Result<ChangePlanResultDto>.Success(
                new ChangePlanResultDto(Pending: false, Blocked: false, null, null, null, null));

        // ── Downgrade: validate current usage fits the target tier limits ──────
        if (targetTier < stripeCurrentTier)
        {
            var limits = TierLimits.Limits[targetTier];

            var payeeCount = await db.Payees.CountAsync(cancellationToken);
            if (payeeCount > limits.MaxPayees)
            {
                logger.LogInformation(
                    "Tenant {TenantId} downgrade to {Target} blocked: {Count} payees > limit {Limit}",
                    tenantId, targetTier, payeeCount, limits.MaxPayees);

                return Result<ChangePlanResultDto>.Success(new ChangePlanResultDto(
                    Pending: false,
                    Blocked: true,
                    BlockedReason: "payees",
                    Current: payeeCount,
                    Limit: limits.MaxPayees,
                    TargetTier: targetTier.ToString()));
            }

            // Only validate plan count when the target tier has a finite cap.
            if (limits.MaxPlans != int.MaxValue)
            {
                var planCount = await db.CompensationPlans.CountAsync(cancellationToken);
                if (planCount > limits.MaxPlans)
                {
                    logger.LogInformation(
                        "Tenant {TenantId} downgrade to {Target} blocked: {Count} plans > limit {Limit}",
                        tenantId, targetTier, planCount, limits.MaxPlans);

                    return Result<ChangePlanResultDto>.Success(new ChangePlanResultDto(
                        Pending: false,
                        Blocked: true,
                        BlockedReason: "plans",
                        Current: planCount,
                        Limit: limits.MaxPlans,
                        TargetTier: targetTier.ToString()));
                }
            }
        }

        // Find the Stripe priceId for the target tier (monthly interval only).
        var plans = await planService.GetPlansAsync(stripeCurrentTier, cancellationToken);
        var targetPlan = plans.FirstOrDefault(p =>
            string.Equals(p.Tier, targetTier.ToString(), StringComparison.OrdinalIgnoreCase)
            && p.PriceId is not null
            && string.Equals(p.Interval, "month", StringComparison.OrdinalIgnoreCase));

        if (targetPlan?.PriceId is null)
        {
            return Result<ChangePlanResultDto>.Failure(
                $"No monthly Stripe price found for tier '{targetTier}'. Contact support.");
        }

        // Call Stripe to update the subscription. The actual tier change in Wasnie
        // happens when the customer.subscription.updated webhook arrives (source of truth).
        bool isUpgrade = targetTier > stripeCurrentTier;
        try
        {
            if (isUpgrade)
            {
                // Upgrade: charges the full new-plan price immediately + resets billing cycle.
                // StripeException is thrown synchronously if the card is declined.
                await stripeManagement.UpgradeSubscriptionAsync(
                    subscription.StripeSubscriptionId!,
                    targetPlan.PriceId,
                    cancellationToken);
            }
            else
            {
                // Downgrade: no immediate charge; proration credit applied to next invoice.
                await stripeManagement.UpdateSubscriptionAsync(
                    subscription.StripeSubscriptionId!,
                    targetPlan.PriceId,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Stripe subscription {Direction} failed for tenant {TenantId} (from {From} to {To})",
                isUpgrade ? "upgrade" : "downgrade", tenantId, stripeCurrentTier, targetTier);

            var errorMessage = isUpgrade
                ? "upgrade_payment_failed"
                : "Failed to update subscription with Stripe. Please try again.";
            return Result<ChangePlanResultDto>.Failure(errorMessage);
        }

        logger.LogInformation(
            "Tenant {TenantId} plan {Direction} from {From} to {To} initiated — awaiting webhook confirmation",
            tenantId, isUpgrade ? "upgrade" : "downgrade", stripeCurrentTier, targetTier);

        return Result<ChangePlanResultDto>.Success(
            new ChangePlanResultDto(Pending: true, Blocked: false, null, null, null, null));
    }
}
