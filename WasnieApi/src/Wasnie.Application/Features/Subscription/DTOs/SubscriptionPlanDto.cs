namespace Wasnie.Application.Features.Subscription.DTOs;

public sealed record SubscriptionPlanDto(
    string?  PriceId,
    string?  ProductId,
    string   Name,
    decimal  Price,
    string   Currency,
    string   Interval,
    string   Tier,
    int      MaxPayees,
    int      MaxPlans,
    bool     IsCurrentPlan);
