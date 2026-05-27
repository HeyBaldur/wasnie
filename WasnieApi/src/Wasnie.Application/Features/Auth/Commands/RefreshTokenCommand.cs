using MediatR;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Commands;

public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<Result<TokenPairDto>>;
