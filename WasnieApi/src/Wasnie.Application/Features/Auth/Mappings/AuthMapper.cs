using Wasnie.Application.Features.Auth.DTOs;

namespace Wasnie.Application.Features.Auth.Mappings;

public static class AuthMapper
{
    public static AuthResultDto ToAuthResultDto(
        string userId,
        string email,
        Guid tenantId,
        string tenantSlug,
        IList<string> roles,
        TokenPairDto tokens) =>
        new(userId, email, tenantId, tenantSlug, roles, tokens);
}
