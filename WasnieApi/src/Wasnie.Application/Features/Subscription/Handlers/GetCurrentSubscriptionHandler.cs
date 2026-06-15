using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Application.Features.Subscription.Queries;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Subscription.Handlers;

public sealed class GetCurrentSubscriptionHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext)
    : IRequestHandler<GetCurrentSubscriptionQuery, Result<UserSubscriptionDto>>
{
    public async Task<Result<UserSubscriptionDto>> Handle(
        GetCurrentSubscriptionQuery request, CancellationToken cancellationToken)
    {
        var sub = await db.UserSubscriptions
            .FirstOrDefaultAsync(s => s.TenantId == tenantContext.TenantId, cancellationToken);

        if (sub is null)
            return Result<UserSubscriptionDto>.Failure("No subscription found for this tenant.");

        return Result<UserSubscriptionDto>.Success(new UserSubscriptionDto(
            Tier: sub.Tier.ToString(),
            Status: sub.Status.ToString(),
            BillingEmail: sub.BillingEmail,
            StripeSubscriptionId: sub.StripeSubscriptionId,
            StripeCustomerId: sub.StripeCustomerId,
            StripePriceId: sub.StripePriceId,
            StripeProductId: sub.StripeProductId,
            CurrentPeriodStart: sub.CurrentPeriodStart,
            CurrentPeriodEnd: sub.CurrentPeriodEnd,
            NextBillingDate: sub.NextBillingDate,
            CanceledAt: sub.CanceledAt,
            CancelAtPeriodEnd: sub.CancelAtPeriodEnd,
            CancelAt: sub.CancelAt,
            CreatedAt: sub.CreatedAt));
    }
}
