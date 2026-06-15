using MediatR;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.Application.Features.Auth.Mappings;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Handlers;

public sealed class LoginCommandHandler(
    IIdentityService identityService,
    ITokenService tokenService,
    IApplicationDbContext dbContext,
    IAuditService auditService)
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

        var emailConfirmed = await identityService.IsEmailConfirmedAsync(userId);
        if (!emailConfirmed)
        {
            return Result<AuthResultDto>.Failure("EMAIL_NOT_CONFIRMED");
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

        // 2FA check: if enabled, issue challenge token instead of full session.
        var twoFactorEnabled = await identityService.IsTwoFactorEnabledAsync(userId);
        if (twoFactorEnabled)
        {
            var challengeToken = tokenService.GenerateTwoFactorChallengeToken(userId);
            return Result<AuthResultDto>.Success(
                AuthMapper.ToTwoFactorChallengeDto(userId, email, tenantId, tenant.Slug, roles, challengeToken));
        }

        var tokens = await tokenService.GenerateTokenPairAsync(userId, email, tenantId, roles);

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantId,
                Action: AuditActions.LoginSuccess,
                ResourceType: ResourceTypes.Auth,
                ResourceId: userId,
                ActorUserId: userId,
                ActorEmail: email,
                DisplayName: email), cancellationToken);
        }
        catch { /* audit failures must not block login */ }

        return Result<AuthResultDto>.Success(
            AuthMapper.ToAuthResultDto(userId, email, tenantId, tenant.Slug, roles, tokens));
    }
}
