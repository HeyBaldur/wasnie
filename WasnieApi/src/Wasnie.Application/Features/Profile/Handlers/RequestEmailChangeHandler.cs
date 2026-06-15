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
using Wasnie.Application.Features.Profile.Commands;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Identity;

namespace Wasnie.Application.Features.Profile.Handlers;

public sealed class RequestEmailChangeHandler(
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IApplicationDbContext dbContext,
    IEmailService emailService,
    IAuditService auditService,
    IOptions<ResendOptions> resendOptions,
    ITenantContext tenantContext,
    IClock clock,
    IGuidGenerator guid,
    ILogger<RequestEmailChangeHandler> logger)
    : IRequestHandler<RequestEmailChangeCommand, Result<bool>>
{
    private static readonly TimeSpan EmailChangeCooldown = TimeSpan.FromMinutes(2);
    private const int MaxEmailChangesPerHour = 5;
    private static readonly TimeSpan TokenExpiry = TimeSpan.FromHours(24);

    public async Task<Result<bool>> Handle(RequestEmailChangeCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            return Result<bool>.Failure("Not authenticated.");

        var currentEmail = currentUser.Email ?? string.Empty;
        var newEmail = request.NewEmail.Trim().ToLowerInvariant();

        if (string.Equals(currentEmail, newEmail, StringComparison.OrdinalIgnoreCase))
            return Result<bool>.Failure("The new email address is the same as your current email.");

        // Check uniqueness — same rule as registration.
        var existing = await identityService.FindUserIdByEmailAsync(newEmail);
        if (existing is not null)
            return Result<bool>.Failure("That email address is already associated with another account.");

        var now = clock.UtcNowOffset;

        // Per-user hourly cap (anti-abuse).
        var sentLastHour = await dbContext.EmailChangeTokens
            .AsNoTracking()
            .CountAsync(t => t.UserId == userId && t.CreatedAt >= now.AddHours(-1), cancellationToken);

        if (sentLastHour >= MaxEmailChangesPerHour)
            return Result<bool>.Failure("Too many email change requests. Please try again later.");

        // Per-user cooldown.
        var recent = await dbContext.EmailChangeTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.UsedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (recent is not null && now - recent.CreatedAt < EmailChangeCooldown)
            return Result<bool>.Failure("Please wait a moment before requesting another email change.");

        // Invalidate all previous unused tokens for this user.
        var previousTokens = await dbContext.EmailChangeTokens
            .Where(t => t.UserId == userId && t.UsedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var prev in previousTokens)
            prev.MarkUsed(now);

        var (rawToken, tokenHash) = GenerateToken();
        var tokenRecord = EmailChangeToken.Create(guid.NewGuid(), userId, newEmail, tokenHash, now.Add(TokenExpiry), now);
        dbContext.EmailChangeTokens.Add(tokenRecord);
        await dbContext.SaveChangesAsync(cancellationToken);

        var confirmUrl = BuildConfirmUrl(resendOptions.Value.FrontendBaseUrl, userId, rawToken);
        var firstName = await identityService.GetClaimAsync(userId, "given_name") ?? newEmail.Split('@')[0];
        var language = await identityService.GetClaimAsync(userId, "locale") ?? "en";

        logger.LogInformation(
            "[DEV] Email change verification link for {UserId}: {ConfirmUrl}", userId, confirmUrl);

        try
        {
            await emailService.SendEmailChangeConfirmationAsync(newEmail, firstName, confirmUrl, language, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email change confirmation to {Email}", newEmail);
            return Result<bool>.Failure("Failed to send confirmation email. Please try again later.");
        }

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantContext.TenantId,
                Action: AuditActions.ProfileEmailChangeRequested,
                ResourceType: ResourceTypes.Auth,
                ResourceId: userId,
                ActorUserId: userId,
                ActorEmail: currentEmail,
                DisplayName: currentEmail,
                Metadata: new Dictionary<string, string> { ["NewEmail"] = newEmail }),
                cancellationToken);
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
        $"{baseUrl.TrimEnd('/')}/profile/confirm-email-change?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";
}
