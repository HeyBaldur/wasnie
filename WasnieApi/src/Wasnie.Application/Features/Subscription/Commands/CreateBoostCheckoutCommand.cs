using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Subscription;

namespace Wasnie.Application.Features.Subscription.Commands;

/// <summary>Buy one boost pack — a one-off Stripe payment (KAN-83).</summary>
/// <param name="ReturnTo">
/// Which screen the purchase started from, so Checkout returns the customer there. A closed set, not a URL: see
/// <see cref="BoostReturnTo"/>. Defaults to billing, which is where an unspecified purchase belongs.
/// </param>
public sealed record CreateBoostCheckoutCommand(string PriceId, BoostReturnTo ReturnTo = BoostReturnTo.Billing)
    : IRequest<Result<CheckoutSessionDto>>;

public sealed class CreateBoostCheckoutHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IAccountAccessReader accessReader,
    IStripeBoostService boosts,
    ILogger<CreateBoostCheckoutHandler> logger)
    : IRequestHandler<CreateBoostCheckoutCommand, Result<CheckoutSessionDto>>
{
    public async Task<Result<CheckoutSessionDto>> Handle(
        CreateBoostCheckoutCommand request, CancellationToken cancellationToken)
    {
        if (!tenantContext.IsResolved)
            return Result<CheckoutSessionDto>.Failure("No tenant for this request.");

        // ★★ THE PRICE MUST BE ONE WE OFFER. The id arrives from the client, and a Stripe price is a public-ish
        // identifier: without this check a caller could check out against any price in the account — including the
        // €299 subscription — through the one-off path, and the webhook would credit them a boost for it.
        var offers = await boosts.GetOffersAsync(cancellationToken);
        if (offers.All(o => o.PriceId != request.PriceId))
        {
            logger.LogWarning(
                "Tenant {TenantId} asked to buy boost price {PriceId}, which is not on sale",
                tenantContext.TenantId, request.PriceId);

            return Result<CheckoutSessionDto>.Failure("The requested boost is not available.");
        }

        // ★ A BOOST TOPS UP A SUBSCRIPTION; IT DOES NOT REPLACE ONE. Letting a trial buy tokens would sell somebody a
        // top-up for an account that is about to lock anyway — what they need is to subscribe (§C3).
        var access = await accessReader.GetAsync(tenantContext.TenantId, cancellationToken);
        if (access?.State != AccountAccessState.Active)
            return Result<CheckoutSessionDto>.Failure("A subscription is required before buying a boost.");

        var billingEmail = await db.UserSubscriptions
            .Where(s => s.TenantId == tenantContext.TenantId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.BillingEmail)
            .FirstOrDefaultAsync(cancellationToken);

        var url = await boosts.CreateBoostCheckoutSessionAsync(
            tenantContext.TenantId,
            request.PriceId,
            string.IsNullOrWhiteSpace(billingEmail) ? currentUser.Email ?? string.Empty : billingEmail,
            request.ReturnTo,
            cancellationToken);

        return Result<CheckoutSessionDto>.Success(new CheckoutSessionDto(url));
    }
}
