using MediatR;

namespace Wasnie.Application.Features.Auth.Commands;

public sealed record LogoutCommand(string UserId) : IRequest<int>;
