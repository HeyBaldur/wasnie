namespace Wasnie.Application.Features.Subscription.DTOs;

public sealed record SubscriptionPlanDto(
    string?  PriceId,
    string?  ProductId,
    string   Name,
    decimal  Price,
    string   Currency,
    string   Interval,
    // The plan code from Billing:Plans (e.g. "pro"). Limits: -1 = unlimited.
    string   PlanCode,
    int      MaxPayees,
    int      MaxPlans,
    bool     IsCurrentPlan);
