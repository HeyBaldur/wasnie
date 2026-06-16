namespace Wasnie.Application.Features.Auth.DTOs;

public sealed record TwoFactorSetupDto(
    string Secret,
    string OtpauthUri);
