namespace Wasnie.Application.Features.Auth.DTOs;

public sealed record TwoFactorStatusDto(
    bool IsEnabled,
    int RecoveryCodeCount);
