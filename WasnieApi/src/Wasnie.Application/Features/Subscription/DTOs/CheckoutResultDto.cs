namespace Wasnie.Application.Features.Subscription.DTOs;

public sealed record CheckoutResultDto(
    string? CheckoutUrl,
    bool Blocked,
    string? BlockedReason,
    int? Current,
    int? Limit,
    string? TargetTier);
