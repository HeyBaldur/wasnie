using MediatR;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Handlers;

public sealed class DisableTwoFactorCommandHandler(
    ICurrentUserService currentUser,
    ITenantContext tenantContext,
    IIdentityService identityService,
    IAuditService auditService)
    : IRequestHandler<DisableTwoFactorCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        DisableTwoFactorCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
            return Result<Unit>.Failure("User not found.");

        // Double verification: password + current TOTP code.
        var passwordValid = await identityService.VerifyPasswordAsync(userId, request.Password);
        if (!passwordValid)
            return Result<Unit>.Failure("Current password is incorrect.");

        var codeValid = await identityService.VerifyTotpCodeAsync(userId, request.Code);
        if (!codeValid)
            return Result<Unit>.Failure("Invalid verification code.");

        var disabled = await identityService.DisableTwoFactorAsync(userId);
        if (!disabled)
            return Result<Unit>.Failure("Could not disable two-factor authentication.");

        var email = currentUser.Email ?? userId;
        var tenantId = tenantContext.TenantId;

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantId,
                Action: AuditActions.TwoFactorDisabled,
                ResourceType: ResourceTypes.Auth,
                ResourceId: userId,
                ActorUserId: userId,
                ActorEmail: email,
                DisplayName: email), cancellationToken);
        }
        catch { }

        return Result<Unit>.Success(Unit.Value);
    }
}
