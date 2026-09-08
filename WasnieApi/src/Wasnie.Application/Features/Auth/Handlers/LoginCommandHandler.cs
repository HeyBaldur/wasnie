using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Options;
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
    IAuditService auditService,
    ILoginAttemptTracker attemptTracker,
    IEmailService emailService,
    IOptions<ResendOptions> resendOptions,
    ILogger<LoginCommandHandler> logger)
    : IRequestHandler<LoginCommand, Result<AuthResultDto>>
{
    // Screen code, not prose: the message the user reads is built in the SPA from this code plus
    // the minutes, so a wording or translation fix does not require redeploying the API.
    public const string AccountLockedCode = "ACCOUNT_LOCKED";

    public async Task<Result<AuthResultDto>> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        var (succeeded, userId, email) = await identityService.ValidateCredentialsAsync(
            request.Email,
            request.Password);

        if (!succeeded || userId is null || email is null)
        {
            return await HandleFailedAttemptAsync(request.Email, cancellationToken);
        }

        attemptTracker.Reset(request.Email);

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

    /// <summary>
    /// The single exit for every failed sign-in. Everything observable from outside — the code,
    /// the minutes, and (via the dummy hash in ValidateCredentialsAsync) roughly the time taken —
    /// is derived only from the attempt counter, which counts any address typed into the form.
    /// Whether the account exists changes only what happens silently: the warning email.
    /// </summary>
    private async Task<Result<AuthResultDto>> HandleFailedAttemptAsync(
        string attemptedEmail,
        CancellationToken cancellationToken)
    {
        var state = attemptTracker.RecordFailure(attemptedEmail);

        if (!state.IsLocked)
        {
            return Result<AuthResultDto>.Failure("Invalid credentials.");
        }

        if (state.JustLocked)
        {
            await TrySendLockoutWarningAsync(attemptedEmail, state.RetryAfterMinutes, cancellationToken);
        }

        return Result<AuthResultDto>.Failure($"{AccountLockedCode}:{state.RetryAfterMinutes}");
    }

    private async Task TrySendLockoutWarningAsync(
        string attemptedEmail,
        int minutes,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = await identityService.FindUserIdByEmailAsync(attemptedEmail);
            if (userId is null)
            {
                // No account: nothing to warn, and nothing about this is visible to the caller.
                return;
            }

            var firstName = await identityService.GetClaimAsync(userId, "given_name")
                            ?? attemptedEmail.Split('@')[0];
            var language = await identityService.GetClaimAsync(userId, "locale") ?? "en";
            var forgotPasswordUrl = $"{resendOptions.Value.FrontendBaseUrl.TrimEnd('/')}/auth/forgot-password";

            await emailService.SendAccountLockedAsync(
                attemptedEmail, firstName, forgotPasswordUrl, minutes, language, cancellationToken);
        }
        catch (Exception ex)
        {
            // A warning we could not deliver must still leave a row someone can see (§B1). It must
            // not change the response either: a login that failed differently when the mail server
            // was down would be its own oracle.
            logger.LogError(ex, "Failed to send account-locked warning to {EmailMask}", MaskEmail(attemptedEmail));
        }
    }

    // "fil***@gmail.com" — enough to correlate abuse, not full exposure in logs.
    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        var prefix = email[..Math.Min(3, at)];
        return $"{prefix}***{email[at..]}";
    }
}
