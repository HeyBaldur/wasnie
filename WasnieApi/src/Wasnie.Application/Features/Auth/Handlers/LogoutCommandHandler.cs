using MediatR;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Auth.Commands;

namespace Wasnie.Application.Features.Auth.Handlers;

public sealed class LogoutCommandHandler(
    ITokenService tokenService,
    ILogger<LogoutCommandHandler> logger)
    : IRequestHandler<LogoutCommand, int>
{
    public async Task<int> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var count = await tokenService.RevokeUserRefreshTokensAsync(request.UserId);
        logger.LogInformation("User {UserId} logged out and {Count} refresh tokens revoked",
            request.UserId, count);
        return count;
    }
}
