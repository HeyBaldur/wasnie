using MediatR;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Commands;

public sealed record EnableTwoFactorCommand(string VerificationCode) : IRequest<Result<EnableTwoFactorResultDto>>;
