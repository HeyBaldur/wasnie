using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Handlers;

public sealed class RefreshTokenCommandHandler(
    ITokenService tokenService,
    IIdentityService identityService)
    : IRequestHandler<RefreshTokenCommand, Result<TokenPairDto>>
{
    public async Task<Result<TokenPairDto>> Handle(
        RefreshTokenCommand request,
        CancellationToken cancellationToken)
    {
        var userId = await tokenService.ValidateRefreshTokenAsync(request.RefreshToken);
        if (userId is null)
        {
            return Result<TokenPairDto>.Failure("Invalid or expired refresh token.");
        }

        var tenantIdString = await identityService.GetTenantIdClaimAsync(userId);
        if (tenantIdString is null || !Guid.TryParse(tenantIdString, out var tenantId))
        {
            return Result<TokenPairDto>.Failure("Tenant association is missing.");
        }

        await tokenService.RevokeRefreshTokenAsync(request.RefreshToken);

        var email = await identityService.FindUserIdByEmailAsync(userId) ?? string.Empty;
        var roles = await identityService.GetUserRolesAsync(userId);
        var tokens = await tokenService.GenerateTokenPairAsync(userId, email, tenantId, roles);

        return Result<TokenPairDto>.Success(tokens);
    }
}
