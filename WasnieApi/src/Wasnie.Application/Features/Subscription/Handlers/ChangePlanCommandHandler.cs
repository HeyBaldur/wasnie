using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription.Commands;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Subscription.Handlers;

public sealed class ChangePlanCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ISubscriptionPlanService planService,
    IStripeSubscriptionManagementService stripeManagement,
    ILogger<ChangePlanCommandHandler> logger)
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

        var currentTier = subscription.Tier;
        if (currentTier == targetTier)
            return Result<ChangePlanResultDto>.Success(
                new ChangePlanResultDto(Pending: false, Blocked: false, null, null, null, null));

        // Downgrade: validate that current usage fits the target tier limits.
        if (targetTier < currentTier)
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
        var plans = await planService.GetPlansAsync(currentTier, cancellationToken);
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
        try
        {
            await stripeManagement.UpdateSubscriptionAsync(
                subscription.StripeSubscriptionId!,
                targetPlan.PriceId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Stripe subscription update failed for tenant {TenantId} (from {From} to {To})",
                tenantId, currentTier, targetTier);
            return Result<ChangePlanResultDto>.Failure(
                "Failed to update subscription with Stripe. Please try again.");
        }

        logger.LogInformation(
            "Tenant {TenantId} plan change from {From} to {To} initiated — awaiting webhook confirmation",
            tenantId, currentTier, targetTier);

        return Result<ChangePlanResultDto>.Success(
            new ChangePlanResultDto(Pending: true, Blocked: false, null, null, null, null));
    }
}
