using MediatR;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.Application.Features.Auth.Mappings;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Handlers;

public sealed class VerifyTwoFactorLoginCommandHandler(
    ITokenService tokenService,
    IIdentityService identityService,
    IApplicationDbContext dbContext,
    IAuditService auditService)
    : IRequestHandler<VerifyTwoFactorLoginCommand, Result<AuthResultDto>>
{
    public async Task<Result<AuthResultDto>> Handle(
        VerifyTwoFactorLoginCommand request,
        CancellationToken cancellationToken)
    {
        var userId = tokenService.ValidateTwoFactorChallengeToken(request.ChallengeToken);
        if (userId is null)
        {
            return Result<AuthResultDto>.Failure("Invalid or expired 2FA session. Please log in again.");
        }

        var email = await identityService.FindEmailByUserIdAsync(userId);
        if (email is null)
        {
            return Result<AuthResultDto>.Failure("User not found.");
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

        bool codeValid;
        bool usedRecoveryCode = false;

        if (request.IsRecoveryCode)
        {
            codeValid = await identityService.RedeemRecoveryCodeAsync(userId, request.Code);
            if (codeValid) usedRecoveryCode = true;
        }
        else
        {
            codeValid = await identityService.VerifyTotpCodeAsync(userId, request.Code);
        }

        if (!codeValid)
        {
            try
            {
                await auditService.LogAsync(new AuditEntry(
                    TenantId: tenantId,
                    Action: AuditActions.TwoFactorLoginFailure,
                    ResourceType: ResourceTypes.Auth,
                    ResourceId: userId,
                    ActorUserId: userId,
                    ActorEmail: email,
                    DisplayName: email), cancellationToken);
            }
            catch { }

            return Result<AuthResultDto>.Failure("Invalid verification code.");
        }

        var roles = await identityService.GetUserRolesAsync(userId);
        var tokens = await tokenService.GenerateTokenPairAsync(userId, email, tenantId, roles);

        try
        {
            if (usedRecoveryCode)
            {
                await auditService.LogAsync(new AuditEntry(
                    TenantId: tenantId,
                    Action: AuditActions.RecoveryCodeUsed,
                    ResourceType: ResourceTypes.Auth,
                    ResourceId: userId,
                    ActorUserId: userId,
                    ActorEmail: email,
                    DisplayName: email), cancellationToken);
            }

            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantId,
                Action: AuditActions.TwoFactorLoginSuccess,
                ResourceType: ResourceTypes.Auth,
                ResourceId: userId,
                ActorUserId: userId,
                ActorEmail: email,
                DisplayName: email), cancellationToken);
        }
        catch { }

        return Result<AuthResultDto>.Success(
            AuthMapper.ToAuthResultDto(userId, email, tenantId, tenant.Slug, roles, tokens));
    }
}
