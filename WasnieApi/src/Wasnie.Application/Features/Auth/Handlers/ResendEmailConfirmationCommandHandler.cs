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

public sealed class ResendEmailConfirmationCommandHandler(
    IApplicationDbContext dbContext,
    IIdentityService identityService,
    IEmailService emailService,
    IAuditService auditService,
    IOptions<ResendOptions> resendOptions,
    IClock clock,
    IGuidGenerator guid,
    ILogger<ResendEmailConfirmationCommandHandler> logger)
    : IRequestHandler<ResendEmailConfirmationCommand, Result<bool>>
{
    // Prevent hammering: one resend per 2 minutes per user.
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(2);
    // Hard cap: no more than 5 confirmation emails per user per hour.
    private const int MaxResendsPerHour = 5;

    public async Task<Result<bool>> Handle(ResendEmailConfirmationCommand request, CancellationToken cancellationToken)
    {
        var userId = await identityService.FindUserIdByEmailAsync(request.Email);
        if (userId is null)
            return Result<bool>.Success(true); // Don't reveal whether email exists.

        var alreadyConfirmed = await identityService.IsEmailConfirmedAsync(userId);
        if (alreadyConfirmed)
            return Result<bool>.Success(true); // No-op.

        var now = clock.UtcNowOffset;

        // Per-email hourly cap: silently block and log (anti-enumeration: same 200 to caller).
        var sentLastHour = await dbContext.EmailConfirmationTokens
            .AsNoTracking()
            .CountAsync(t => t.UserId == userId && t.CreatedAt >= now.AddHours(-1), cancellationToken);

        if (sentLastHour >= MaxResendsPerHour)
        {
            logger.LogWarning(
                "Resend hourly cap reached for {EmailMask} ({Count}/{Max})",
                MaskEmail(request.Email), sentLastHour, MaxResendsPerHour);
            return Result<bool>.Success(true);
        }

        // Per-email cooldown: silently block and log (anti-enumeration: same 200 to caller).
        var recentToken = await dbContext.EmailConfirmationTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.UsedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (recentToken is not null && now - recentToken.CreatedAt < ResendCooldown)
        {
            logger.LogWarning(
                "Resend cooldown active for {EmailMask} (issued {SecondsAgo}s ago)",
                MaskEmail(request.Email), (int)(now - recentToken.CreatedAt).TotalSeconds);
            return Result<bool>.Success(true);
        }

        var firstName = await identityService.GetClaimAsync(userId, "given_name") ?? request.Email.Split('@')[0];

        var (rawToken, tokenHash) = GenerateToken();
        var tokenRecord = EmailConfirmationToken.Create(guid.NewGuid(), userId, tokenHash, now.AddHours(48), now);
        dbContext.EmailConfirmationTokens.Add(tokenRecord);
        await dbContext.SaveChangesAsync(cancellationToken);

        var confirmUrl = BuildConfirmUrl(resendOptions.Value.FrontendBaseUrl, userId, rawToken);
        var language = await identityService.GetClaimAsync(userId, "locale") ?? "en";

        // Log before sending so manual confirmation is always possible even if Resend fails.
        logger.LogInformation(
            "[DEV] Email confirmation link for {Email}: {ConfirmUrl}", request.Email, confirmUrl);

        try
        {
            await emailService.SendEmailConfirmationAsync(request.Email, firstName, confirmUrl, language, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Resend failed: could not send confirmation email to {Email}", request.Email);
            return Result<bool>.Failure("Failed to send confirmation email. Please try again later.");
        }

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: Guid.Empty,
                Action: AuditActions.EmailConfirmationSent,
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

    private static string BuildConfirmUrl(string baseUrl, string userId, string token) =>
        $"{baseUrl.TrimEnd('/')}/auth/confirm-email?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";

    // Returns "fil***@gmail.com" — enough for abuse correlation, not full exposure in logs.
    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        var prefix = email[..Math.Min(3, at)];
        return $"{prefix}***{email[at..]}";
    }
}
