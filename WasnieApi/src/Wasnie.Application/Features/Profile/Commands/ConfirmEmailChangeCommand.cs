using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Profile.Commands;

public sealed record ConfirmEmailChangeCommand(
    string UserId,
    string Token) : IRequest<Result<bool>>;
