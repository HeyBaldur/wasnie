namespace Wasnie.Application.Features.Auth.DTOs;

public sealed record EnableTwoFactorResultDto(
    IEnumerable<string> RecoveryCodes);
