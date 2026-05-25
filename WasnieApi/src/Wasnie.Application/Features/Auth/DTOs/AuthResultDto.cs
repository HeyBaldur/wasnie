namespace Wasnie.Application.Features.Auth.DTOs;

public sealed record AuthResultDto(
    string UserId,
    string Email,
    Guid TenantId,
    string TenantSlug,
    IList<string> Roles,
    TokenPairDto Tokens);
