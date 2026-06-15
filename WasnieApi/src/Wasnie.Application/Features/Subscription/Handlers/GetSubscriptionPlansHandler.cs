using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Application.Features.Subscription.Queries;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Subscription.Handlers;

public sealed class GetSubscriptionPlansHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ISubscriptionPlanService planService)
    : IRequestHandler<GetSubscriptionPlansQuery, Result<IReadOnlyList<SubscriptionPlanDto>>>
{
    public async Task<Result<IReadOnlyList<SubscriptionPlanDto>>> Handle(
        GetSubscriptionPlansQuery request, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, cancellationToken);

        if (tenant is null)
            return Result<IReadOnlyList<SubscriptionPlanDto>>.Failure("Tenant not found.");

        // StripeUnavailableException propagates to the middleware → 503.
        var plans = await planService.GetPlansAsync(tenant.Tier, cancellationToken);
        return Result<IReadOnlyList<SubscriptionPlanDto>>.Success(plans);
    }
}
