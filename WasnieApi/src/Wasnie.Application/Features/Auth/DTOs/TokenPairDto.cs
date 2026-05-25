namespace Wasnie.Application.Features.Auth.DTOs;

public sealed record TokenPairDto(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt);
