using MediatR;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Handlers;

public sealed class RegenerateRecoveryCodesCommandHandler(
    ICurrentUserService currentUser,
    ITenantContext tenantContext,
    IIdentityService identityService,
    IAuditService auditService)
    : IRequestHandler<RegenerateRecoveryCodesCommand, Result<IEnumerable<string>>>
{
    private const int RecoveryCodeCount = 10;

    public async Task<Result<IEnumerable<string>>> Handle(
        RegenerateRecoveryCodesCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
            return Result<IEnumerable<string>>.Failure("User not found.");

        var isEnabled = await identityService.IsTwoFactorEnabledAsync(userId);
        if (!isEnabled)
            return Result<IEnumerable<string>>.Failure("Two-factor authentication is not enabled.");

        var passwordValid = await identityService.VerifyPasswordAsync(userId, request.Password);
        if (!passwordValid)
            return Result<IEnumerable<string>>.Failure("Current password is incorrect.");

        var codeValid = await identityService.VerifyTotpCodeAsync(userId, request.Code);
        if (!codeValid)
            return Result<IEnumerable<string>>.Failure("Invalid verification code.");

        var codes = await identityService.GenerateRecoveryCodesAsync(userId, RecoveryCodeCount);
        if (codes is null)
            return Result<IEnumerable<string>>.Failure("Could not generate recovery codes.");

        var email = currentUser.Email ?? userId;
        var tenantId = tenantContext.TenantId;

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantId,
                Action: AuditActions.RecoveryCodesRegenerated,
                ResourceType: ResourceTypes.Auth,
                ResourceId: userId,
                ActorUserId: userId,
                ActorEmail: email,
                DisplayName: email), cancellationToken);
        }
        catch { }

        return Result<IEnumerable<string>>.Success(codes);
    }
}
