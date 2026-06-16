using MediatR;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Handlers;

public sealed class EnableTwoFactorCommandHandler(
    ICurrentUserService currentUser,
    ITenantContext tenantContext,
    IIdentityService identityService,
    IAuditService auditService)
    : IRequestHandler<EnableTwoFactorCommand, Result<EnableTwoFactorResultDto>>
{
    public async Task<Result<EnableTwoFactorResultDto>> Handle(
        EnableTwoFactorCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
            return Result<EnableTwoFactorResultDto>.Failure("User not found.");

        var (succeeded, recoveryCodes) = await identityService.EnableTwoFactorAsync(userId, request.VerificationCode);
        if (!succeeded)
            return Result<EnableTwoFactorResultDto>.Failure("Invalid verification code. Please scan the QR code again and try.");

        var email = currentUser.Email ?? userId;
        var tenantId = tenantContext.TenantId;

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantId,
                Action: AuditActions.TwoFactorEnabled,
                ResourceType: ResourceTypes.Auth,
                ResourceId: userId,
                ActorUserId: userId,
                ActorEmail: email,
                DisplayName: email), cancellationToken);
        }
        catch { }

        return Result<EnableTwoFactorResultDto>.Success(new EnableTwoFactorResultDto(recoveryCodes));
    }
}
