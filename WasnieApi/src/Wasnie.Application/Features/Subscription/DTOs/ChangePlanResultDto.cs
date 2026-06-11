namespace Wasnie.Application.Features.Subscription.DTOs;

public sealed record ChangePlanResultDto(
    bool Pending,
    bool Blocked,
    string? BlockedReason,
    int? Current,
    int? Limit,
    string? TargetTier);
