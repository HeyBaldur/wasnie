using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription.Commands;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Subscription.Handlers;

public sealed class CreateCheckoutSessionHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    ISubscriptionPlanService planService,
    ISubscriptionPlanCatalog catalog,
    IStripeCheckoutService checkoutService,
    IStripeSubscriptionReconciler reconciler,
    ILogger<CreateCheckoutSessionHandler> logger)
    : IRequestHandler<CreateCheckoutSessionCommand, Result<CheckoutResultDto>>
{
    public const string AlreadySubscribedReason = "AlreadySubscribed";

    public async Task<Result<CheckoutResultDto>> Handle(
        CreateCheckoutSessionCommand request, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, cancellationToken);

        if (tenant is null)
            return Result<CheckoutResultDto>.Failure("Tenant not found.");

        // The priceId must be one the plan list offers right now — active price, active product, a known plan.
        // A price outside it (archived product, unknown plan) is refused before Stripe is ever asked.
        var plans = await planService.GetPlansAsync(tenant.PlanCode, cancellationToken);
        var offered = plans.FirstOrDefault(p => p.PriceId == request.PriceId);

        if (offered is null)
            return Result<CheckoutResultDto>.Failure("The requested plan is not available.");

        var plan = catalog.Find(offered.PlanCode);
        if (plan is null)
            return Result<CheckoutResultDto>.Failure("The requested plan is not available.");

        // Current usage must fit the plan being bought (dormant while the only plan is unlimited).
        var excess = await PlanUsageFit.CheckAsync(db, plan, cancellationToken);
        if (excess is not null)
        {
            logger.LogInformation(
                "Tenant {TenantId} checkout blocked: {Count} {Reason} exceeds {Limit} (target plan {Plan})",
                tenantContext.TenantId, excess.Current, excess.Reason, excess.Limit, plan.Code);

            return Result<CheckoutResultDto>.Success(new CheckoutResultDto(
                CheckoutUrl: null,
                Blocked: true,
                BlockedReason: excess.Reason,
                Current: excess.Current,
                Limit: excess.Limit,
                TargetPlanCode: plan.Code));
        }

        // ★★ NEVER A SECOND SUBSCRIPTION (KAN-77). The stored row may be stale — a lost webhook left a customer who had
        // just paid looking "cancelled" — so it is brought in line with Stripe first; if the tenant already has a live
        // subscription, a new checkout would charge them twice.
        await reconciler.ReconcileAsync(cancellationToken);
        var current = await db.UserSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantContext.TenantId, cancellationToken);
        if (current is { StripeSubscriptionId: not null }
            && current.Status is Domain.Subscription.SubscriptionStatus.Active
                or Domain.Subscription.SubscriptionStatus.PastDue
                or Domain.Subscription.SubscriptionStatus.Trialing)
        {
            logger.LogWarning(
                "Tenant {TenantId} checkout refused: already subscribed ({SubscriptionId})",
                tenantContext.TenantId, current.StripeSubscriptionId);

            return Result<CheckoutResultDto>.Success(new CheckoutResultDto(
                CheckoutUrl: null,
                Blocked: true,
                BlockedReason: AlreadySubscribedReason,
                Current: null,
                Limit: null,
                TargetPlanCode: plan.Code));
        }

        var checkoutUrl = await checkoutService.CreateCheckoutSessionAsync(
            tenantId: tenantContext.TenantId,
            priceId: request.PriceId,
            billingEmail: currentUser.Email ?? string.Empty,
            cancellationToken: cancellationToken);

        return Result<CheckoutResultDto>.Success(new CheckoutResultDto(
            CheckoutUrl: checkoutUrl,
            Blocked: false,
            BlockedReason: null,
            Current: null,
            Limit: null,
            TargetPlanCode: null));
    }
}
