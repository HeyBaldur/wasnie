using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.Domain.Identity;

namespace Wasnie.Infrastructure.Services;

public sealed class TokenService(
    IConfiguration configuration,
    IApplicationDbContext db,
    IClock clock,
    IGuidGenerator guid) : ITokenService
{
    private const int RefreshTokenLifetimeDays = 7;

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
        var expiryMinutes = int.TryParse(jwtSettings["ExpiryMinutes"], out var m) ? m : 15;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = clock.UtcNowOffset;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, guid.NewGuid().ToString()),
            new("tenant_id", tenantId.ToString())
        };

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var accessTokenExpiresAt = now.AddMinutes(expiryMinutes);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: accessTokenExpiresAt.UtcDateTime,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        var rawRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshTokenExpiresAt = now.AddDays(RefreshTokenLifetimeDays);

        var refreshToken = RefreshToken.Create(rawRefreshToken, userId, tenantId, refreshTokenExpiresAt, guid.NewGuid(), now);
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

        if (entry is null || !entry.IsValidAt(clock.UtcNowOffset))
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

        entry.Revoke(clock.UtcNowOffset);
        await db.SaveChangesAsync();
    }

    public async Task<int> RevokeUserRefreshTokensAsync(string userId)
    {
        var tokens = await db.RefreshTokens
            .Where(r => r.UserId == userId && !r.IsRevoked)
            .ToListAsync();

        var now = clock.UtcNowOffset;
        foreach (var token in tokens)
        {
            token.Revoke(now);
        }

        if (tokens.Count > 0)
        {
            await db.SaveChangesAsync();
        }

        return tokens.Count;
    }

    public string GenerateTwoFactorChallengeToken(string userId)
    {
        var jwtSettings = configuration.GetSection("JwtSettings");
        var secret = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret not configured.");
        var issuer = jwtSettings["Issuer"] ?? "WasnieApi";
        var audience = jwtSettings["Audience"] ?? "WasnieUi";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = clock.UtcNowOffset;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Jti, guid.NewGuid().ToString()),
            new("purpose", "2fa_challenge"),
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.AddMinutes(5).UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string? ValidateTwoFactorChallengeToken(string challengeToken)
    {
        var jwtSettings = configuration.GetSection("JwtSettings");
        var secret = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret not configured.");
        var issuer = jwtSettings["Issuer"] ?? "WasnieApi";
        var audience = jwtSettings["Audience"] ?? "WasnieUi";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear(); // prevent "sub" → ClaimTypes.NameIdentifier remapping

        try
        {
            var principal = handler.ValidateToken(challengeToken, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.Zero,
            }, out _);

            var purpose = principal.FindFirst("purpose")?.Value;
            if (purpose != "2fa_challenge") return null;

            return principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        }
        catch
        {
            return null;
        }
    }
}
