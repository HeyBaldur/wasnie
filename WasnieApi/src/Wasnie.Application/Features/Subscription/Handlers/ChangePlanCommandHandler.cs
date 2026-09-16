using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription.Commands;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Subscription.Handlers;

/// <summary>
/// Moves a paying tenant to another plan of the catalog (KAN-77: plans are codes from configuration, not the
/// old Starter/Growth/Scale enum). Today the catalog has one plan, so every request is either "already on it"
/// or an unknown code; the flow is kept whole for the day a second plan is configured.
/// </summary>
public sealed class ChangePlanCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ISubscriptionPlanService planService,
    ISubscriptionPlanCatalog catalog,
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
        var targetPlan = catalog.Find(request.TargetPlanCode);
        if (targetPlan is null)
        {
            return Result<ChangePlanResultDto>.Failure(
                $"'{request.TargetPlanCode}' is not a plan that can be subscribed to.");
        }

        var tenantId = tenantContext.TenantId;

        var subscription = await db.UserSubscriptions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

        if (subscription is null)
            return Result<ChangePlanResultDto>.Failure("No subscription found.");

        if (string.IsNullOrEmpty(subscription.StripeSubscriptionId))
            return Result<ChangePlanResultDto>.Failure("No active paid Stripe subscription to change.");

        // ── Read the authoritative plan from Stripe ───────────────────────────
        // The DB plan can be stale: it only updates when customer.subscription.updated arrives (seconds to
        // minutes after a prior change). Trusting the DB would route the wrong branch (upgrade treated as
        // downgrade or vice versa) and either miss a charge or add an unwanted proration.
        string stripeCurrentPlanCode;
        try
        {
            var planFromStripe = await stripeManagement.GetCurrentPlanCodeFromStripeAsync(
                subscription.StripeSubscriptionId!, cancellationToken);

            if (planFromStripe is null)
            {
                logger.LogError(
                    "Stripe subscription {SubId} for tenant {TenantId} has no mappable plan — aborting plan change",
                    subscription.StripeSubscriptionId, tenantId);
                return Result<ChangePlanResultDto>.Failure("plan_change_unavailable");
            }

            stripeCurrentPlanCode = planFromStripe;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to read current plan from Stripe for subscription {SubId} (tenant {TenantId})",
                subscription.StripeSubscriptionId, tenantId);
            return Result<ChangePlanResultDto>.Failure("plan_change_unavailable");
        }

        // ── Sync DB if stale ──────────────────────────────────────────────────
        if (!string.Equals(subscription.PlanCode, stripeCurrentPlanCode, StringComparison.OrdinalIgnoreCase))
        {
            var stalePlan = subscription.PlanCode;
            subscription.SyncPlan(stripeCurrentPlanCode, clock.UtcNowOffset);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Synced stale DB plan for tenant {TenantId}: {OldPlan} → {NewPlan} (source: Stripe)",
                tenantId, stalePlan, stripeCurrentPlanCode);

            await audit.LogAsync(new AuditEntry(
                TenantId:     tenantId,
                Action:       AuditActions.SubscriptionTierSyncedFromStripe,
                ResourceType: "UserSubscription",
                ResourceId:   subscription.Id.ToString(),
                ActorUserId:  "SYSTEM",
                ActorEmail:   "system@wasnie.io",
                DisplayName:  subscription.BillingEmail,
                Before:       new { Plan = stalePlan },
                After:        new { Plan = stripeCurrentPlanCode },
                Metadata:     new Dictionary<string, string>
                {
                    ["stripeSubscriptionId"] = subscription.StripeSubscriptionId!,
                    ["trigger"]             = "ChangePlanCommand",
                }), cancellationToken);
        }

        // ── Same-plan guard ───────────────────────────────────────────────────
        if (string.Equals(stripeCurrentPlanCode, targetPlan.Code, StringComparison.OrdinalIgnoreCase))
            return Result<ChangePlanResultDto>.Success(
                new ChangePlanResultDto(Pending: false, Blocked: false, null, null, null, null));

        var isUpgrade = catalog.IsUpgrade(catalog.Find(stripeCurrentPlanCode), targetPlan);

        // ── Downgrade: current usage must fit the target plan ─────────────────
        if (!isUpgrade)
        {
            var excess = await PlanUsageFit.CheckAsync(db, targetPlan, cancellationToken);
            if (excess is not null)
            {
                logger.LogInformation(
                    "Tenant {TenantId} downgrade to {Target} blocked: {Count} {Reason} > limit {Limit}",
                    tenantId, targetPlan.Code, excess.Current, excess.Reason, excess.Limit);

                return Result<ChangePlanResultDto>.Success(new ChangePlanResultDto(
                    Pending: false,
                    Blocked: true,
                    BlockedReason: excess.Reason,
                    Current: excess.Current,
                    Limit: excess.Limit,
                    TargetPlanCode: targetPlan.Code));
            }
        }

        // Find the Stripe price for the target plan (monthly interval only).
        var plans = await planService.GetPlansAsync(stripeCurrentPlanCode, cancellationToken);
        var targetPrice = plans.FirstOrDefault(p =>
            string.Equals(p.PlanCode, targetPlan.Code, StringComparison.OrdinalIgnoreCase)
            && p.PriceId is not null
            && string.Equals(p.Interval, "month", StringComparison.OrdinalIgnoreCase));

        if (targetPrice?.PriceId is null)
        {
            return Result<ChangePlanResultDto>.Failure(
                $"No monthly Stripe price found for plan '{targetPlan.Code}'. Contact support.");
        }

        // Call Stripe to update the subscription. The actual plan change in Wasnie happens when the
        // customer.subscription.updated webhook arrives (source of truth).
        try
        {
            if (isUpgrade)
            {
                // Upgrade: charges the full new-plan price immediately + resets billing cycle.
                // StripeException is thrown synchronously if the card is declined.
                await stripeManagement.UpgradeSubscriptionAsync(
                    subscription.StripeSubscriptionId!, targetPrice.PriceId, cancellationToken);
            }
            else
            {
                // Downgrade: no immediate charge; proration credit applied to next invoice.
                await stripeManagement.UpdateSubscriptionAsync(
                    subscription.StripeSubscriptionId!, targetPrice.PriceId, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Stripe subscription {Direction} failed for tenant {TenantId} (from {From} to {To})",
                isUpgrade ? "upgrade" : "downgrade", tenantId, stripeCurrentPlanCode, targetPlan.Code);

            var errorMessage = isUpgrade
                ? "upgrade_payment_failed"
                : "Failed to update subscription with Stripe. Please try again.";
            return Result<ChangePlanResultDto>.Failure(errorMessage);
        }

        logger.LogInformation(
            "Tenant {TenantId} plan {Direction} from {From} to {To} initiated — awaiting webhook confirmation",
            tenantId, isUpgrade ? "upgrade" : "downgrade", stripeCurrentPlanCode, targetPlan.Code);

        return Result<ChangePlanResultDto>.Success(
            new ChangePlanResultDto(Pending: true, Blocked: false, null, null, null, null));
    }
}
