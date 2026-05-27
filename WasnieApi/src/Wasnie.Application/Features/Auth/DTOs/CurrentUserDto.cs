namespace Wasnie.Application.Features.Auth.DTOs;

public sealed record CurrentUserDto(
    string UserId,
    string Email,
    string Role,
    Guid TenantId,
    string TenantSlug,
    string Tier,
    IReadOnlyList<string> Permissions);
