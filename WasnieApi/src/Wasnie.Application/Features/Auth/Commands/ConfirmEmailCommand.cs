using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Commands;

public sealed record ConfirmEmailCommand(string UserId, string Token) : IRequest<Result<bool>>;
