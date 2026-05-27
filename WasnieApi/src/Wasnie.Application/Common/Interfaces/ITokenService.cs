using Wasnie.Application.Features.Auth.DTOs;

namespace Wasnie.Application.Common.Interfaces;

public interface ITokenService
{
    Task<TokenPairDto> GenerateTokenPairAsync(string userId, string email, Guid tenantId, IList<string> roles);
    Task<string?> ValidateRefreshTokenAsync(string refreshToken);
    Task RevokeRefreshTokenAsync(string refreshToken);
    Task<int> RevokeUserRefreshTokensAsync(string userId);
}
