using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Identity;

namespace Wasnie.Application.Features.Auth.Handlers;

/// <summary>
/// Sends an administrator the Organization identifier(s) of the workspaces they administer (KAN-93).
///
/// ★★ EVERY EXIT IS THE SAME SUCCESS, AND THAT IS THE WHOLE SECURITY PROPERTY. Address unknown,
/// address known but not an administrator, administrator of nothing, mail server down — all of them
/// return <c>Success(true)</c> and the screen says the same sentence. The moment one of these answers
/// differently, the endpoint becomes a directory: feed it addresses, read the difference, and learn
/// which companies use Incentra and who administers them. The identifier is the key to the sign-in
/// form, so that list is exactly what an attacker wants.
///
/// ★★ IT IS RESTRICTED TO ADMINISTRATORS, AND THE RESTRICTION IS INVISIBLE. Only somebody holding
/// TenantAdmin in an active membership gets an email. A rep who forgot the identifier is told by the
/// screen to ask their administrator — which is the honest instruction, because in this product the
/// identifier is the administrator's to hand out.
///
/// ★★ THE MEMBERSHIPS ARE THE SOURCE, WITH THE CLAIM AS A FALLBACK — the same pair
/// <c>LoginCommandHandler</c> uses, and for the same reason. The founder of a workspace created
/// before KAN-91's backfill may have no membership row at all, and the founder is precisely the person
/// most likely to be standing at this screen. Reading only the table would have refused the one case
/// this feature exists for.
///
/// ★ THE COOLDOWN IS IN MEMORY, NOT A TABLE. The IP-partitioned limiter on the route stops volume;
/// this stops one address being mailed repeatedly from many IPs. It is deliberately NOT a
/// <c>PasswordResetToken</c>-style row: nothing here is a token, nothing has to be redeemed, and a
/// migration to remember "we already said this" would outlive its usefulness. A restart forgetting a
/// cooldown costs one extra email.
/// </summary>
public sealed class RequestOrganizationIdentifierHandler(
    IApplicationDbContext db,
    IIdentityService identityService,
    IEmailService emailService,
    IAuditService auditService,
    IMemoryCache cache,
    IOptions<ResendOptions> resendOptions,
    ILogger<RequestOrganizationIdentifierHandler> logger)
    : IRequestHandler<RequestOrganizationIdentifierCommand, Result<bool>>
{
    /// <summary>One email per address per five minutes, whatever the source IP.</summary>
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    public async Task<Result<bool>> Handle(
        RequestOrganizationIdentifierCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var cacheKey = $"org-identifier-recovery:{email.ToLowerInvariant()}";

        if (cache.TryGetValue(cacheKey, out _))
        {
            logger.LogInformation(
                "Organization identifier recovery: cooldown active for {EmailMask}", MaskEmail(email));
            return Result<bool>.Success(true);
        }

        var userId = await identityService.FindUserIdByEmailAsync(email);
        if (userId is null)
        {
            logger.LogInformation(
                "Organization identifier recovery: no account for {EmailMask} - silent success",
                MaskEmail(email));
            return Result<bool>.Success(true);
        }

        // An unconfirmed mailbox has not been proved to belong to this person, so it is not a channel
        // we may send a tenant's identifier to. Same rule, same silence, as the password reset.
        if (!await identityService.IsEmailConfirmedAsync(userId))
        {
            logger.LogWarning(
                "Organization identifier recovery: unconfirmed account {EmailMask}", MaskEmail(email));
            return Result<bool>.Success(true);
        }

        var organizations = await ResolveAdministeredOrganizationsAsync(userId, cancellationToken);

        if (organizations.Count == 0)
        {
            // ★ NOT AN ADMINISTRATOR ANYWHERE. Logged so somebody supporting a confused user can see
            // the attempt happened and tell them the truth — §B1: a thing the system declined to do
            // leaves a row.
            logger.LogInformation(
                "Organization identifier recovery: {EmailMask} administers no workspace - no email sent",
                MaskEmail(email));
            return Result<bool>.Success(true);
        }

        // Set BEFORE sending: a send that throws must still have consumed the cooldown, or a failing
        // mail server turns into an unthrottled retry loop against the same address.
        cache.Set(cacheKey, true, Cooldown);

        var firstName = await identityService.GetClaimAsync(userId, "given_name") ?? email.Split('@')[0];
        var language = await identityService.GetClaimAsync(userId, "locale") ?? "en";
        var loginUrl = $"{resendOptions.Value.FrontendBaseUrl.TrimEnd('/')}/auth/login";

        logger.LogInformation(
            "[DEV] Organization identifier(s) for {Email}: {Slugs}",
            email, string.Join(", ", organizations.Select(o => o.Slug)));

        try
        {
            await emailService.SendOrganizationIdentifierAsync(
                email, firstName, organizations, loginUrl, language, cancellationToken);
        }
        catch (Exception ex)
        {
            // ★ THE RESPONSE DOES NOT CHANGE. A failure that answered differently would be an oracle
            // of its own — it only fires for addresses that got as far as having something to send.
            logger.LogError(ex, "Failed to send organization identifier email to {EmailMask}", MaskEmail(email));
            return Result<bool>.Success(true);
        }

        foreach (var org in organizations)
        {
            try
            {
                await auditService.LogAsync(new AuditEntry(
                    TenantId: org.TenantId,
                    Action: AuditActions.OrganizationIdentifierRequested,
                    ResourceType: ResourceTypes.Auth,
                    ResourceId: userId,
                    ActorUserId: userId,
                    ActorEmail: email,
                    DisplayName: email), cancellationToken);
            }
            catch { /* audit must not block */ }
        }

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// The workspaces this account ADMINISTERS, as name and identifier.
    ///
    /// ★ ACTIVE MEMBERSHIPS ONLY. Somebody whose access was switched off is not an administrator of
    /// that workspace any more, whatever the role column still says — the same reading
    /// <c>LastAdminGuard</c> applies when it counts admins.
    ///
    /// ★ <c>IgnoreQueryFilters</c> THROUGHOUT: nobody is authenticated here, so there is no tenant for
    /// the global filter to scope to and it would match nothing at all.
    /// </summary>
    private async Task<IReadOnlyList<OrganizationIdentifier>> ResolveAdministeredOrganizationsAsync(
        string userId, CancellationToken cancellationToken)
    {
        var adminTenantIds = await db.TenantUsers
            .IgnoreQueryFilters()
            .Where(TenantUser.ActiveSpec)
            .Where(u => u.UserId == userId && u.Role == Roles.TenantAdmin)
            .Select(u => u.TenantId)
            .ToListAsync(cancellationToken);

        if (adminTenantIds.Count == 0)
        {
            // The founder with no membership row — see the class comment. The claim names one
            // workspace and Identity's own roles say what they are in it.
            var claimed = await identityService.GetTenantIdClaimAsync(userId);
            if (claimed is null || !Guid.TryParse(claimed, out var claimedTenantId))
                return [];

            var roles = await identityService.GetUserRolesAsync(userId);
            if (!roles.Any(r => string.Equals(r, Roles.TenantAdmin, StringComparison.OrdinalIgnoreCase)))
                return [];

            adminTenantIds = [claimedTenantId];
        }

        return await db.Tenants
            .IgnoreQueryFilters()
            .Where(t => adminTenantIds.Contains(t.Id) && t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new OrganizationIdentifier(t.Id, t.Name, t.Slug))
            .ToListAsync(cancellationToken);
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
