using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription.Commands;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Subscription.Handlers;

public sealed class CreateCheckoutSessionHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    ISubscriptionPlanService planService,
    IStripeCheckoutService checkoutService,
    ILogger<CreateCheckoutSessionHandler> logger)
    : IRequestHandler<CreateCheckoutSessionCommand, Result<CheckoutResultDto>>
{
    public async Task<Result<CheckoutResultDto>> Handle(
        CreateCheckoutSessionCommand request, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, cancellationToken);

        if (tenant is null)
            return Result<CheckoutResultDto>.Failure("Tenant not found.");

        // Validate the priceId belongs to a known paid tier.
        var plans = await planService.GetPlansAsync(tenant.Tier, cancellationToken);
        var plan = plans.FirstOrDefault(p => p.PriceId == request.PriceId);

        if (plan is null)
            return Result<CheckoutResultDto>.Failure("The requested plan is not available.");

        if (plan.Tier == "Free")
            return Result<CheckoutResultDto>.Failure("Use /select-free for the Free plan.");

        // Resolve the target Tier enum so we can look up its limits.
        if (!Enum.TryParse<Tier>(plan.Tier, ignoreCase: true, out var targetTier))
            return Result<CheckoutResultDto>.Failure($"Unknown tier '{plan.Tier}'.");

        var limits = TierLimits.Limits[targetTier];

        // Validate that current usage fits the target tier (same pattern as downgrade in ChangePlanCommandHandler).
        var payeeCount = await db.Payees.CountAsync(cancellationToken);
        if (payeeCount > limits.MaxPayees)
        {
            logger.LogInformation(
                "Tenant {TenantId} checkout blocked: {Count} payees exceeds {Limit} (target tier {Tier})",
                tenantContext.TenantId, payeeCount, limits.MaxPayees, targetTier);

            return Result<CheckoutResultDto>.Success(new CheckoutResultDto(
                CheckoutUrl: null,
                Blocked: true,
                BlockedReason: "payees",
                Current: payeeCount,
                Limit: limits.MaxPayees,
                TargetTier: targetTier.ToString()));
        }

        if (limits.MaxPlans != int.MaxValue)
        {
            var planCount = await db.CompensationPlans.CountAsync(cancellationToken);
            if (planCount > limits.MaxPlans)
            {
                logger.LogInformation(
                    "Tenant {TenantId} checkout blocked: {Count} plans exceeds {Limit} (target tier {Tier})",
                    tenantContext.TenantId, planCount, limits.MaxPlans, targetTier);

                return Result<CheckoutResultDto>.Success(new CheckoutResultDto(
                    CheckoutUrl: null,
                    Blocked: true,
                    BlockedReason: "plans",
                    Current: planCount,
                    Limit: limits.MaxPlans,
                    TargetTier: targetTier.ToString()));
            }
        }

        var billingEmail = currentUser.Email ?? string.Empty;

        var checkoutUrl = await checkoutService.CreateCheckoutSessionAsync(
            tenantId: tenantContext.TenantId,
            priceId: request.PriceId,
            billingEmail: billingEmail,
            cancellationToken: cancellationToken);

        return Result<CheckoutResultDto>.Success(new CheckoutResultDto(
            CheckoutUrl: checkoutUrl,
            Blocked: false,
            BlockedReason: null,
            Current: null,
            Limit: null,
            TargetTier: null));
    }
}
