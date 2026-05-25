using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Common.Results;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.Application.Features.Auth.Mappings;

namespace Wasnie.Application.Features.Auth.Handlers;

public sealed class LoginCommandHandler(
    IIdentityService identityService,
    ITokenService tokenService,
    IApplicationDbContext dbContext)
    : IRequestHandler<LoginCommand, Result<AuthResultDto>>
{
    public async Task<Result<AuthResultDto>> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        var (succeeded, userId, email) = await identityService.ValidateCredentialsAsync(
            request.Email,
            request.Password);

        if (!succeeded || userId is null || email is null)
        {
            return Result<AuthResultDto>.Failure("Invalid credentials.");
        }

        var tenantIdString = await identityService.GetTenantIdClaimAsync(userId);
        if (tenantIdString is null || !Guid.TryParse(tenantIdString, out var tenantId))
        {
            return Result<AuthResultDto>.Failure("User is not associated with a tenant.");
        }

        var tenant = dbContext.Tenants.FirstOrDefault(t => t.Id == tenantId);
        if (tenant is null || !tenant.IsActive)
        {
            return Result<AuthResultDto>.Failure("Tenant is inactive or not found.");
        }

        var roles = await identityService.GetUserRolesAsync(userId);
        var tokens = await tokenService.GenerateTokenPairAsync(userId, email, tenantId, roles);

        return Result<AuthResultDto>.Success(
            AuthMapper.ToAuthResultDto(userId, email, tenantId, tenant.Slug, roles, tokens));
    }
}
