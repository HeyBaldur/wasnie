using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.Domain.Identity;

namespace Wasnie.Infrastructure.Services;

public sealed class TokenService(IConfiguration configuration, IApplicationDbContext db) : ITokenService
{
    private const int RefreshTokenLifetimeDays = 30;

    public async Task<TokenPairDto> GenerateTokenPairAsync(
        string userId,
        string email,
        Guid tenantId,
        IList<string> roles)
    {
        var jwtSettings = configuration.GetSection("JwtSettings");
        var secret = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret not configured.");
        var issuer = jwtSettings["Issuer"] ?? "WasnieApi";
        var audience = jwtSettings["Audience"] ?? "WasnieUi";
        var expiryMinutes = int.TryParse(jwtSettings["ExpiryMinutes"], out var m) ? m : 60;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("tenant_id", tenantId.ToString())
        };

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: accessTokenExpiresAt.UtcDateTime,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        var rawRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(RefreshTokenLifetimeDays);

        var refreshToken = RefreshToken.Create(rawRefreshToken, userId, tenantId, refreshTokenExpiresAt);
        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync();

        return new TokenPairDto(
            accessToken,
            rawRefreshToken,
            accessTokenExpiresAt,
            refreshTokenExpiresAt);
    }

    public async Task<string?> ValidateRefreshTokenAsync(string refreshToken)
    {
        var entry = await db.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == refreshToken);

        if (entry is null || !entry.IsValid)
        {
            return null;
        }

        return entry.UserId;
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken)
    {
        var entry = await db.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == refreshToken);

        if (entry is null)
        {
            return;
        }

        entry.Revoke();
        await db.SaveChangesAsync();
    }
}
