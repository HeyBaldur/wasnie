namespace Wasnie.Application.Features.Subscription.DTOs;

public sealed record UserSubscriptionDto(
    string? PlanCode,
    string Status,
    string BillingEmail,
    string? StripeSubscriptionId,
    string? StripeCustomerId,
    string? StripePriceId,
    string? StripeProductId,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CanceledAt,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CancelAt,
    DateTimeOffset CreatedAt);
