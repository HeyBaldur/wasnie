using MediatR;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Profile.Commands;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Profile.Handlers;

public sealed class ChangePasswordHandler(
    ICurrentUserService currentUser,
    IIdentityService identityService,
    ITokenService tokenService,
    IAuditService auditService,
    ITenantContext tenantContext)
    : IRequestHandler<ChangePasswordCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            return Result<bool>.Failure("Not authenticated.");

        if (request.NewPassword != request.ConfirmNewPassword)
            return Result<bool>.Failure("New passwords do not match.");

        if (!IsPasswordStrong(request.NewPassword))
            return Result<bool>.Failure(
                "Password must be at least 10 characters and include an uppercase letter, a digit, and a special character.");

        var (succeeded, errors) = await identityService.ChangePasswordAsync(
            userId, request.CurrentPassword, request.NewPassword);

        if (!succeeded)
        {
            var msg = errors.Count > 0 ? errors[0] : "Current password is incorrect.";
            return Result<bool>.Failure(msg);
        }

        // Revoke all refresh tokens — expels other sessions after password change.
        await tokenService.RevokeUserRefreshTokensAsync(userId);

        var email = currentUser.Email ?? string.Empty;

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantContext.TenantId,
                Action: AuditActions.ProfilePasswordChanged,
                ResourceType: ResourceTypes.Auth,
                ResourceId: userId,
                ActorUserId: userId,
                ActorEmail: email,
                DisplayName: email),
                cancellationToken);
        }
        catch { /* audit must not block */ }

        return Result<bool>.Success(true);
    }

    private static bool IsPasswordStrong(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 10) return false;
        if (!password.Any(char.IsUpper)) return false;
        if (!password.Any(char.IsDigit)) return false;
        if (!password.Any(c => !char.IsLetterOrDigit(c))) return false;
        return true;
    }
}
