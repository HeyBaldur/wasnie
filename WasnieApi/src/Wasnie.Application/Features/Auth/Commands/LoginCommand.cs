using MediatR;
using Wasnie.Domain.Common.Results;
using Wasnie.Application.Features.Auth.DTOs;

namespace Wasnie.Application.Features.Auth.Commands;

public sealed record LoginCommand(
    string Email,
    string Password) : IRequest<Result<AuthResultDto>>;
