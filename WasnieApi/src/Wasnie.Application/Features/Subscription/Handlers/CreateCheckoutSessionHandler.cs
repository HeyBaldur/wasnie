using MediatR;
using Microsoft.EntityFrameworkCore;
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
    IStripeCheckoutService checkoutService)
    : IRequestHandler<CreateCheckoutSessionCommand, Result<CheckoutSessionDto>>
{
    public async Task<Result<CheckoutSessionDto>> Handle(
        CreateCheckoutSessionCommand request, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, cancellationToken);

        if (tenant is null)
            return Result<CheckoutSessionDto>.Failure("Tenant not found.");

        // Validate the priceId belongs to a known paid tier
        var plans = await planService.GetPlansAsync(tenant.Tier, cancellationToken);
        var plan = plans.FirstOrDefault(p => p.PriceId == request.PriceId);

        if (plan is null)
            return Result<CheckoutSessionDto>.Failure("The requested plan is not available.");

        if (plan.Tier == "Free")
            return Result<CheckoutSessionDto>.Failure("Use /select-free for the Free plan.");

        var billingEmail = currentUser.Email ?? string.Empty;

        var checkoutUrl = await checkoutService.CreateCheckoutSessionAsync(
            tenantId: tenantContext.TenantId,
            priceId: request.PriceId,
            billingEmail: billingEmail,
            cancellationToken: cancellationToken);

        return Result<CheckoutSessionDto>.Success(new CheckoutSessionDto(checkoutUrl));
    }
}
