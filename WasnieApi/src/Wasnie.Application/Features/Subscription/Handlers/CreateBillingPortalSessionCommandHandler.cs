using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Subscription.Commands;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Subscription.Handlers;

public sealed class CreateBillingPortalSessionCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    IStripeSubscriptionManagementService stripeManagement,
    IOptions<StripeOptions> stripeOptions,
    ILogger<CreateBillingPortalSessionCommandHandler> logger)
    : IRequestHandler<CreateBillingPortalSessionCommand, Result<BillingPortalSessionDto>>
{
    public async Task<Result<BillingPortalSessionDto>> Handle(
        CreateBillingPortalSessionCommand request,
        CancellationToken cancellationToken)
    {
        var subscription = await db.UserSubscriptions
            .FirstOrDefaultAsync(s => s.TenantId == tenantContext.TenantId, cancellationToken);

        if (subscription is null)
            return Result<BillingPortalSessionDto>.Failure("No subscription found.");

        if (string.IsNullOrEmpty(subscription.StripeCustomerId))
            return Result<BillingPortalSessionDto>.Failure(
                "No Stripe customer associated with this subscription.");

        var returnUrl = $"{stripeOptions.Value.FrontendBaseUrl}/subscription";

        try
        {
            var url = await stripeManagement.CreateBillingPortalSessionAsync(
                subscription.StripeCustomerId,
                returnUrl,
                cancellationToken);

            return Result<BillingPortalSessionDto>.Success(new BillingPortalSessionDto(url));
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to create Stripe Billing Portal session for tenant {TenantId}",
                tenantContext.TenantId);
            return Result<BillingPortalSessionDto>.Failure(
                "Failed to open the billing portal. Please try again.");
        }
    }
}
