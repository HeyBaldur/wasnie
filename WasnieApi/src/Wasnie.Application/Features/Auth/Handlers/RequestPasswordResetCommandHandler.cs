using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Identity;

namespace Wasnie.Application.Features.Auth.Handlers;

public sealed class RequestPasswordResetCommandHandler(
    IApplicationDbContext dbContext,
    IIdentityService identityService,
    IEmailService emailService,
    IAuditService auditService,
    IOptions<ResendOptions> resendOptions,
    IClock clock,
    IGuidGenerator guid,
    ILogger<RequestPasswordResetCommandHandler> logger)
    : IRequestHandler<RequestPasswordResetCommand, Result<bool>>
{
    // Prevent hammering: one request per 2 minutes per user.
    private static readonly TimeSpan ResetCooldown = TimeSpan.FromMinutes(2);
    // Hard cap: no more than 5 reset emails per user per hour.
    private const int MaxResetsPerHour = 5;
    // Token expiry: 30 minutes — reset tokens are high-value.
    private static readonly TimeSpan TokenExpiry = TimeSpan.FromMinutes(30);

    public async Task<Result<bool>> Handle(RequestPasswordResetCommand request, CancellationToken cancellationToken)
    {
        var userId = await identityService.FindUserIdByEmailAsync(request.Email);
        if (userId is null)
        {
            logger.LogInformation(
                "[DEV] Password reset: no account found for {EmailMask} — silent success returned",
                MaskEmail(request.Email));
            return Result<bool>.Success(true); // Anti-enumeration: don't reveal whether email exists.
        }

        // If email is not confirmed, silently return success (anti-enumeration).
        // User must confirm email before they can reset password.
        var emailConfirmed = await identityService.IsEmailConfirmedAsync(userId);
        if (!emailConfirmed)
        {
            logger.LogWarning(
                "Password reset requested for unconfirmed account {EmailMask}",
                MaskEmail(request.Email));
            return Result<bool>.Success(true);
        }

        var now = clock.UtcNowOffset;

        // Per-user hourly cap (anti-abuse).
        var sentLastHour = await dbContext.PasswordResetTokens
            .AsNoTracking()
            .CountAsync(t => t.UserId == userId && t.CreatedAt >= now.AddHours(-1), cancellationToken);

        if (sentLastHour >= MaxResetsPerHour)
        {
            logger.LogWarning(
                "Password reset hourly cap reached for {EmailMask} ({Count}/{Max})",
                MaskEmail(request.Email), sentLastHour, MaxResetsPerHour);
            return Result<bool>.Success(true);
        }

        // Per-user cooldown.
        var recentToken = await dbContext.PasswordResetTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.UsedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (recentToken is not null && now - recentToken.CreatedAt < ResetCooldown)
        {
            logger.LogWarning(
                "Password reset cooldown active for {EmailMask} (issued {SecondsAgo}s ago)",
                MaskEmail(request.Email), (int)(now - recentToken.CreatedAt).TotalSeconds);
            return Result<bool>.Success(true);
        }

        // Invalidate all previous unused tokens for this user (security: each request supersedes prior ones).
        var previousTokens = await dbContext.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var prev in previousTokens)
        {
            prev.MarkUsed(now);
        }

        var (rawToken, tokenHash) = GenerateToken();
        var tokenRecord = PasswordResetToken.Create(guid.NewGuid(), userId, tokenHash, now.Add(TokenExpiry), now);
        dbContext.PasswordResetTokens.Add(tokenRecord);
        await dbContext.SaveChangesAsync(cancellationToken);

        var resetUrl = BuildResetUrl(resendOptions.Value.FrontendBaseUrl, userId, rawToken);
        var firstName = await identityService.GetClaimAsync(userId, "given_name") ?? request.Email.Split('@')[0];
        var language = await identityService.GetClaimAsync(userId, "locale") ?? "en";

        // Log before sending so link is always available even if email delivery fails.
        logger.LogInformation(
            "[DEV] Password reset link for {Email}: {ResetUrl}", request.Email, resetUrl);

        try
        {
            await emailService.SendPasswordResetAsync(request.Email, firstName, resetUrl, language, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send password reset email to {Email}", request.Email);
            return Result<bool>.Failure("Failed to send password reset email. Please try again later.");
        }

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: Guid.Empty,
                Action: AuditActions.PasswordResetRequested,
                ResourceType: ResourceTypes.Auth,
                ResourceId: userId,
                ActorUserId: userId,
                ActorEmail: request.Email,
                DisplayName: request.Email), cancellationToken);
        }
        catch { /* audit must not block */ }

        return Result<bool>.Success(true);
    }

    private static (string Raw, string Hash) GenerateToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return (raw, hash);
    }

    private static string BuildResetUrl(string baseUrl, string userId, string token) =>
        $"{baseUrl.TrimEnd('/')}/auth/reset-password?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";

    // Returns "fil***@gmail.com" — enough for abuse correlation, not full exposure in logs.
    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        var prefix = email[..Math.Min(3, at)];
        return $"{prefix}***{email[at..]}";
    }
}
