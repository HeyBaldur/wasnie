using MediatR;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>
    /// KAN-32. An administrator closed this account's access. Deliberately NOT the same code as
    /// ACCOUNT_LOCKED: locked is temporary and clears itself, deactivated is a decision somebody made
    /// and only somebody can undo. Telling the reader to "try again in 15 minutes" would be a lie.
    /// </summary>
    public const string AccountDeactivatedCode = "ACCOUNT_DEACTIVATED";

    /// <summary>
    /// KAN-91. The address belongs to more than one workspace and the caller did not say which.
    ///
    /// It is not an error, it is a question, and it may safely differ from "invalid credentials": the
    /// password was already proved correct before this point, so nothing here is disclosed to anybody
    /// who does not already hold the account.
    ///
    /// THE PROPERTY THAT ACTUALLY MATTERS is narrower, and this code has it: an identifier naming an
    /// organization that does NOT EXIST and one naming an organization the account is NOT IN give the
    /// SAME answer. Without that, somebody holding one password could walk the slug space and
    /// enumerate every company using Incentra. An earlier comment here claimed the stronger property -
    /// that a wrong identifier reads like a wrong password - and that was never true of the code.
    /// </summary>
    public const string OrganizationRequiredCode = "ORGANIZATION_REQUIRED";

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

        // KAN-91. WHICH WORKSPACE THIS SESSION IS FOR IS DECIDED HERE, AND ONLY HERE. Everything
        // downstream - ITenantContext, AuthorizationService, every query filter - reads the TOKEN, so
        // choosing the membership at this line is the whole of the change. Nothing else had to move.
        //
        // IgnoreQueryFilters throughout: nobody is authenticated yet, so the context has no tenant and
        // the filter would match nothing.
        var memberships = await dbContext.TenantUsers
            .IgnoreQueryFilters()
            .Where(u => u.UserId == userId)
            .ToListAsync(cancellationToken);

        // DEACTIVATED MEMBERSHIPS ARE DROPPED, NOT REFUSED, and the difference matters now that a
        // person can hold several. Somebody switched off in one workspace and active in another must
        // still get into the second; only when NONE is open is this a deactivated account.
        var open = memberships.Where(m => m.IsActive).ToList();

        if (memberships.Count > 0 && open.Count == 0)
        {
            logger.LogInformation("Sign-in refused: every membership of this account is deactivated.");
            return Result<AuthResultDto>.Failure(AccountDeactivatedCode);
        }

        var identifierGiven = !string.IsNullOrWhiteSpace(request.OrganizationId);
        var selected = await SelectMembershipAsync(open, request.OrganizationId, cancellationToken);

        // AN IDENTIFIER THAT WAS TYPED AND DID NOT MATCH IS ALWAYS A REFUSAL, whatever the account's
        // membership count. The first cut only refused when there was more than one, so somebody with
        // a single workspace who mistyped their organization was silently signed into it anyway -
        // the product quietly ignoring what they had just told it. Caught by an integration test.
        if (selected is null && (identifierGiven || open.Count > 1))
        {
            // Not a failure: the caller has to say which workspace. No workspace is NAMED in the
            // answer - that list is private, and returning it would tell whoever holds this password
            // every company the person works for.
            return Result<AuthResultDto>.Failure(OrganizationRequiredCode);
        }

        Guid tenantId;
        IList<string> roles;

        if (selected is not null)
        {
            tenantId = selected.TenantId;
            roles = [selected.Role];
        }
        else if (open.Count == 0)
        {
            // NO MEMBERSHIP ROW AT ALL - an account created before KAN-91's backfill, or one the
            // backfill skipped because it had no role. It falls back to the claim, exactly as before
            // this ticket. Treating a missing row as "no access" would lock out anybody the migration
            // could not map, which is the failure mode the backfill was written to avoid.
            var claimed = await identityService.GetTenantIdClaimAsync(userId);
            if (claimed is null || !Guid.TryParse(claimed, out tenantId))
                return Result<AuthResultDto>.Failure("User is not associated with a tenant.");

            roles = await identityService.GetUserRolesAsync(userId);
        }
        else
        {
            // Exactly one open membership and no identifier given: the ordinary case, and the one
            // every user of the product has today.
            tenantId = open[0].TenantId;
            roles = [open[0].Role];
        }

        var tenant = dbContext.Tenants.FirstOrDefault(t => t.Id == tenantId);
        if (tenant is null || !tenant.IsActive)
        {
            return Result<AuthResultDto>.Failure("Tenant is inactive or not found.");
        }


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

    /// <summary>
    /// Picks the membership this session is for: the one matching the organization identifier, or the
    /// only one there is.
    ///
    /// AN IDENTIFIER THAT MATCHES NOTHING RETURNS NULL AND THE CALLER REFUSES, which makes it
    /// indistinguishable from a wrong password. If "no such organization" read differently from "wrong
    /// password", anyone could enumerate which companies use Incentra - and, with one address, which of
    /// them a person works for. That is the property KAN-21 built the single failure exit for, and this
    /// path stays inside it.
    ///
    /// The slug is trimmed before comparison: it is typed by a human out of an email or a chat
    /// message, and refusing "wasnie-ldta-polska " would be refusing the right answer.
    /// </summary>
    private async Task<Wasnie.Domain.Identity.TenantUser?> SelectMembershipAsync(
        IReadOnlyList<Wasnie.Domain.Identity.TenantUser> open,
        string? organizationId,
        CancellationToken cancellationToken)
    {
        if (open.Count == 0) return null;

        if (string.IsNullOrWhiteSpace(organizationId))
            return open.Count == 1 ? open[0] : null;

        var slug = organizationId.Trim();

        var tenantId = await dbContext.Tenants
            .Where(t => t.Slug == slug)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return tenantId is null
            ? null
            : open.FirstOrDefault(m => m.TenantId == tenantId.Value);
    }
}
