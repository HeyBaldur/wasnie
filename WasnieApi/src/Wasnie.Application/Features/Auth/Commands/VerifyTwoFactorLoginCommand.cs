using MediatR;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Commands;

public sealed record VerifyTwoFactorLoginCommand(
    string ChallengeToken,
    string Code,
    bool IsRecoveryCode = false) : IRequest<Result<AuthResultDto>>;
