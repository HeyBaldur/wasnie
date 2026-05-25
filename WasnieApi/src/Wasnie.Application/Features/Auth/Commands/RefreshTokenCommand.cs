using MediatR;
using Wasnie.Domain.Common.Results;
using Wasnie.Application.Features.Auth.DTOs;

namespace Wasnie.Application.Features.Auth.Commands;

public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<Result<TokenPairDto>>;
