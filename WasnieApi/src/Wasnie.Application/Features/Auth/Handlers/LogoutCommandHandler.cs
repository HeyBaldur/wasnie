using MediatR;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Domain.Audit;

namespace Wasnie.Application.Features.Auth.Handlers;

public sealed class LogoutCommandHandler(
    ITokenService tokenService,
    ILogger<LogoutCommandHandler> logger,
    ITenantContext tenantContext,
    IAuditService auditService)
    : IRequestHandler<LogoutCommand, int>
{
    public async Task<int> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var count = await tokenService.RevokeUserRefreshTokensAsync(request.UserId);
        logger.LogInformation("User {UserId} logged out and {Count} refresh tokens revoked",
            request.UserId, count);

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantContext.TenantId,
                Action: AuditActions.Logout,
                ResourceType: ResourceTypes.Auth,
                ResourceId: request.UserId,
                ActorUserId: request.UserId,
                ActorEmail: string.Empty,
                DisplayName: request.UserId), cancellationToken);
        }
        catch { /* audit failures must not block logout */ }

        return count;
    }
}
