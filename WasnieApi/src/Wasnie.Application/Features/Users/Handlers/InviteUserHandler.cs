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
using Wasnie.Application.Features.Users.Commands;
using Wasnie.Application.Features.Users.DTOs;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;
using Wasnie.Domain.Identity;

namespace Wasnie.Application.Features.Users.Handlers;

/// <summary>
/// Creates an invitation and sends it (KAN-32).
///
/// ★★ THE SEAT IS TAKEN HERE, NOT AT ACCEPTANCE, and <see cref="SeatUsage"/> explains why: a check
/// that only ran when the person clicked would refuse THEM for something only the admin can fix.
///
/// ★★ THE EMAIL IS SENT AFTER THE ROW IS COMMITTED, AND A FAILED SEND DOES NOT UNDO IT. An invitation
/// that exists with nobody notified is repairable — the admin sees it Pending and presses Resend. The
/// opposite order gives a token that was emailed and matches nothing, which no one can repair because
/// nobody can see it. Same choice as the password reset path.
/// </summary>
public sealed class InviteUserHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    ITierLimitChecker tierLimitChecker,
    IEmailService emailService,
    IAuditService auditService,
    IOptions<ResendOptions> resendOptions,
    IClock clock,
    IGuidGenerator guid,
    ILogger<InviteUserHandler> logger)
    : IRequestHandler<InviteUserCommand, Result<InvitationDto>>
{
    /// <summary>
    /// How long an invitation lives.
    ///
    /// ★ SEVEN DAYS, NOT THE THIRTY MINUTES A PASSWORD RESET GETS. A reset is answered by somebody
    /// already sitting at the screen that asked for it; an invitation is read by a person who may be
    /// on holiday, and a link that dies over a long weekend turns into a support request. It is still
    /// bounded, because an invitation that never expires is a permanent key to the tenant.
    /// </summary>
    public const int ExpiryDays = 7;

    public async Task<Result<InvitationDto>> Handle(
        InviteUserCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.UsersManage, cancellationToken);

        var email = request.Email.Trim();
        var normalised = Invitation.Normalise(email);
        var now = clock.UtcNowOffset;

        if (!Roles.IsAssignable(request.Role))
            throw new DomainCodedException(InvitationRefusal.RoleUnknown,
                new Dictionary<string, object?> { ["role"] = request.Role });

        // ── Already one of us? ────────────────────────────────────────────────
        // KAN-91. AN ADDRESS THAT BELONGS TO ANOTHER WORKSPACE IS PERFECTLY INVITABLE, and the first
        // cut of this handler refused it. That was wrong twice over: it blocked the real case of one
        // person working across two departments, and it answered "already a member" about a workspace
        // the admin cannot see. Only membership of THIS workspace is a reason to refuse.
        //
        // It reads the claim as well as TenantUsers because a tenant founded before KAN-32 has the
        // claim and no membership row. B44 backfilled every existing account, so today the two agree;
        // reading both keeps a future gap from turning into a duplicate invitation.
        var existingUserId = await identityService.FindUserIdByEmailAsync(email);
        if (existingUserId is not null)
        {
            var alreadyHere = await db.TenantUsers
                .AnyAsync(u => u.UserId == existingUserId, cancellationToken);

            if (!alreadyHere)
            {
                var claimed = await identityService.GetTenantIdClaimAsync(existingUserId);
                alreadyHere = claimed == tenantContext.TenantId.ToString();
            }

            if (alreadyHere)
                throw new DomainCodedException(InvitationRefusal.EmailAlreadyMember,
                    new Dictionary<string, object?> { ["email"] = email });
        }

        // ── Already invited? ──────────────────────────────────────────────────
        var outstanding = await db.Invitations
            .Where(Invitation.PendingSpec(now))
            .AnyAsync(i => i.Email == normalised, cancellationToken);

        if (outstanding)
            throw new DomainCodedException(InvitationRefusal.EmailAlreadyInvited,
                new Dictionary<string, object?> { ["email"] = email });

        // ── Room for one more? ────────────────────────────────────────────────
        var seats = await tierLimitChecker.GetSeatUsageAsync(cancellationToken);
        if (!seats.HasRoom)
            throw new DomainCodedException(InvitationRefusal.NoSeatsAvailable,
                new Dictionary<string, object?> { ["used"] = seats.Used, ["limit"] = seats.Limit });

        var (rawToken, tokenHash) = GenerateToken();

        var invitation = Invitation.Create(
            guid.NewGuid(),
            tenantContext.TenantId,
            email,
            request.Role,
            tokenHash,
            currentUser.UserId ?? string.Empty,
            now.AddDays(ExpiryDays),
            now);

        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(new AuditEntry(
            TenantId: tenantContext.TenantId,
            Action: AuditActions.UserInvited,
            ResourceType: "Invitation",
            ResourceId: invitation.Id.ToString(),
            ActorUserId: currentUser.UserId ?? string.Empty,
            ActorEmail: currentUser.Email ?? string.Empty,
            DisplayName: email,
            Metadata: new Dictionary<string, string> { ["role"] = request.Role }),
            cancellationToken);

        await SendAsync(invitation, rawToken, email, cancellationToken);

        return Result<InvitationDto>.Success(InvitationMapper.ToDto(invitation, now, currentUser.Email));
    }

    private async Task SendAsync(
        Invitation invitation, string rawToken, string email, CancellationToken cancellationToken)
    {
        var companyName = await db.Tenants
            .Where(t => t.Id == invitation.TenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "Incentra";

        var inviterName = currentUser.Email ?? "An administrator";
        var acceptUrl = BuildAcceptUrl(resendOptions.Value.FrontendBaseUrl, rawToken);

        logger.LogInformation("[DEV] Invitation link for {Email}: {AcceptUrl}", email, acceptUrl);

        try
        {
            await emailService.SendInvitationAsync(
                email, inviterName, companyName, acceptUrl, ExpiryDays, "en", cancellationToken);
        }
        catch (Exception ex)
        {
            // ★ THE INVITATION SURVIVES A FAILED SEND (§B1). It is on the screen as Pending with a
            // Resend button, which is a state somebody can see and act on — unlike a row rolled back
            // after the email already left.
            logger.LogError(ex,
                "The invitation {InvitationId} was created but its email could not be sent.", invitation.Id);
        }
    }

    internal static string BuildAcceptUrl(string baseUrl, string token) =>
        $"{baseUrl.TrimEnd('/')}/auth/accept-invitation?token={Uri.EscapeDataString(token)}";

    /// <summary>
    /// The raw token goes in the email; only its hash is stored. Same construction as the password
    /// reset path, so a leaked database cannot be used to accept invitations.
    /// </summary>
    internal static (string Raw, string Hash) GenerateToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").Replace("=", string.Empty);
        return (raw, HashToken(raw));
    }

    internal static string HashToken(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
}
