using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Domain.Authorization;

namespace Wasnie.Application.Common.Interfaces;

public interface ISubscriptionPlanService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(Tier currentTier, CancellationToken cancellationToken = default);
}
