namespace Wasnie.Application.Features.Subscription.DTOs;

public sealed record UserSubscriptionDto(
    string Tier,
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
    DateTimeOffset CreatedAt);
