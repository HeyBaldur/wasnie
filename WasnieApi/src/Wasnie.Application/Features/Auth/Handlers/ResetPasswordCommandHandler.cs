using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Handlers;

public sealed class ResetPasswordCommandHandler(
    IApplicationDbContext dbContext,
    IIdentityService identityService,
    ITokenService tokenService,
    IAuditService auditService,
    IClock clock)
    : IRequestHandler<ResetPasswordCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset;
        var tokenHash = ComputeHash(request.Token);

        var record = await dbContext.PasswordResetTokens
            .FirstOrDefaultAsync(
                t => t.UserId == request.UserId && t.TokenHash == tokenHash,
                cancellationToken);

        if (record is null || !record.IsValid(now))
            return Result<bool>.Failure("Password reset link is invalid or has expired.");

        // Validate new password strength (mirrors Identity rules: uppercase + digit + min 10 chars).
        if (!IsPasswordStrong(request.NewPassword))
            return Result<bool>.Failure(
                "Password must be at least 10 characters and include an uppercase letter, a digit, and a special character.");

        // Reset the password via Identity.
        var succeeded = await identityService.ResetPasswordAsync(request.UserId, request.NewPassword);
        if (!succeeded)
            return Result<bool>.Failure("Failed to reset password. Please request a new link.");

        // Mark token as used (prevents replay).
        record.MarkUsed(now);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Revoke all refresh tokens — expels attacker if the account was compromised.
        await tokenService.RevokeUserRefreshTokensAsync(request.UserId);

        var email = await identityService.FindEmailByUserIdAsync(request.UserId) ?? string.Empty;

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: Guid.Empty,
                Action: AuditActions.PasswordResetCompleted,
                ResourceType: ResourceTypes.Auth,
                ResourceId: request.UserId,
                ActorUserId: request.UserId,
                ActorEmail: email,
                DisplayName: email), cancellationToken);
        }
        catch { /* audit must not block */ }

        return Result<bool>.Success(true);
    }

    private static string ComputeHash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
