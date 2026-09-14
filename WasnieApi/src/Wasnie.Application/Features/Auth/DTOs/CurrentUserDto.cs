namespace Wasnie.Application.Features.Auth.DTOs;

public sealed record CurrentUserDto(
    string UserId,
    string Email,
    string Role,
    Guid TenantId,
    string TenantSlug,
    // KAN-77: the tenant subscribed plan code (e.g. "pro"), null while in trial or never subscribed.
    // Replaces the old Tier; account ACCESS (trial/active/locked) is GET /api/subscription/access.
    string? PlanCode,
    bool HasSelectedPlan,
    bool EmailConfirmed,
    bool IsQualified,
    IReadOnlyList<string> Permissions);
