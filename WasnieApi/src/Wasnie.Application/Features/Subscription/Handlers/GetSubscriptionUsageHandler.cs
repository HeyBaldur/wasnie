using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Application.Features.Subscription.Queries;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Subscription.Handlers;

public sealed class GetSubscriptionUsageHandler(IApplicationDbContext db)
    : IRequestHandler<GetSubscriptionUsageQuery, Result<SubscriptionUsageDto>>
{
    public async Task<Result<SubscriptionUsageDto>> Handle(
        GetSubscriptionUsageQuery request, CancellationToken cancellationToken)
    {
        var payeeCount = await db.Payees.CountAsync(cancellationToken);
        var planCount = await db.CompensationPlans.CountAsync(cancellationToken);
        return Result<SubscriptionUsageDto>.Success(new SubscriptionUsageDto(payeeCount, planCount));
    }
}
