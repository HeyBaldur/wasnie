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
using Wasnie.Application.Features.Users.Queries;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;
using Wasnie.Domain.Identity;

namespace Wasnie.Application.Features.Users.Handlers;

/// <summary>
/// Sends an outstanding invitation again, with a fresh token and a fresh deadline (KAN-32).
///
/// ★ THE OLD LINK DIES. See <c>Invitation.Resend</c>: renewing something while the previous key still
/// opens the door is not what renewing looks like to the person pressing the button.
/// </summary>
public sealed class ResendInvitationHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IEmailService emailService,
    IAuditService auditService,
    IOptions<ResendOptions> resendOptions,
    IClock clock,
    ILogger<ResendInvitationHandler> logger)
    : IRequestHandler<ResendInvitationCommand, Result<InvitationDto>>
{
    public async Task<Result<InvitationDto>> Handle(
        ResendInvitationCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.UsersManage, cancellationToken);

        var now = clock.UtcNowOffset;

        var invitation = await db.Invitations
            .FirstOrDefaultAsync(i => i.Id == request.InvitationId, cancellationToken);

        if (invitation is null)
            return Result<InvitationDto>.Failure("Invitation not found.");

        if (!invitation.IsPendingAt(now))
            throw new DomainCodedException(RefusalFor(invitation.StatusAt(now)));

        var (rawToken, tokenHash) = InviteUserHandler.GenerateToken();
        invitation.Resend(tokenHash, now.AddDays(InviteUserHandler.ExpiryDays), now);

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(new AuditEntry(
            TenantId: tenantContext.TenantId,
            Action: AuditActions.InvitationResent,
            ResourceType: "Invitation",
            ResourceId: invitation.Id.ToString(),
            ActorUserId: currentUser.UserId ?? string.Empty,
            ActorEmail: currentUser.Email ?? string.Empty,
            DisplayName: invitation.Email),
            cancellationToken);

        var companyName = await db.Tenants
            .Where(t => t.Id == invitation.TenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "Incentra";

        var acceptUrl = InviteUserHandler.BuildAcceptUrl(resendOptions.Value.FrontendBaseUrl, rawToken);
        logger.LogInformation("[DEV] Invitation link for {Email}: {AcceptUrl}", invitation.Email, acceptUrl);

        try
        {
            await emailService.SendInvitationAsync(
                invitation.Email, currentUser.Email ?? "An administrator", companyName,
                acceptUrl, InviteUserHandler.ExpiryDays, "en", cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The invitation {InvitationId} was renewed but its email failed.", invitation.Id);
        }

        return Result<InvitationDto>.Success(
            InvitationMapper.ToDto(invitation, now, currentUser.Email));
    }

    /// <summary>The refusal that matches a non-pending status, so the reader is told which one it was.</summary>
    internal static string RefusalFor(InvitationStatus status) => status switch
    {
        InvitationStatus.Accepted => InvitationRefusal.TokenAlreadyUsed,
        InvitationStatus.Revoked => InvitationRefusal.TokenRevoked,
        InvitationStatus.Expired => InvitationRefusal.TokenExpired,
        _ => InvitationRefusal.TokenInvalid,
    };
}

/// <summary>
/// Withdraws an outstanding invitation (KAN-32). The row stays; only <c>RevokedAt</c> is written, and
/// the seat is released because the pending spec stops matching it.
/// </summary>
public sealed class RevokeInvitationHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IAuditService auditService,
    IClock clock)
    : IRequestHandler<RevokeInvitationCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        RevokeInvitationCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.UsersManage, cancellationToken);

        var now = clock.UtcNowOffset;

        var invitation = await db.Invitations
            .FirstOrDefaultAsync(i => i.Id == request.InvitationId, cancellationToken);

        if (invitation is null)
            return Result<bool>.Failure("Invitation not found.");

        if (!invitation.IsPendingAt(now))
            throw new DomainCodedException(ResendInvitationHandler.RefusalFor(invitation.StatusAt(now)));

        invitation.Revoke(currentUser.UserId ?? string.Empty, now);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(new AuditEntry(
            TenantId: tenantContext.TenantId,
            Action: AuditActions.InvitationRevoked,
            ResourceType: "Invitation",
            ResourceId: invitation.Id.ToString(),
            ActorUserId: currentUser.UserId ?? string.Empty,
            ActorEmail: currentUser.Email ?? string.Empty,
            DisplayName: invitation.Email),
            cancellationToken);

        return Result<bool>.Success(true);
    }
}

/// <summary>
/// What the public accept page may know before anybody signs in (KAN-32).
///
/// ★ IT ANSWERS THE SAME REFUSALS AS ACCEPTING. A page that loaded happily and only failed on submit
/// would let somebody type a password into a form that was never going to work.
/// </summary>
public sealed class GetInvitationByTokenHandler(
    IApplicationDbContext db,
    IIdentityService identityService,
    IClock clock)
    : IRequestHandler<GetInvitationByTokenQuery, Result<InvitationPreviewDto>>
{
    public async Task<Result<InvitationPreviewDto>> Handle(
        GetInvitationByTokenQuery request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset;
        var tokenHash = InviteUserHandler.HashToken(request.Token);

        // Public route: no session, therefore no tenant. See AcceptInvitationHandler.
        var invitation = await db.Invitations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);

        if (invitation is null)
            throw new DomainCodedException(InvitationRefusal.TokenInvalid);

        if (!invitation.IsPendingAt(now))
            throw new DomainCodedException(ResendInvitationHandler.RefusalFor(invitation.StatusAt(now)));

        var companyName = await db.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.Id == invitation.TenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "Incentra";

        var inviterEmail = await identityService.FindEmailByUserIdAsync(invitation.InvitedBy);

        return Result<InvitationPreviewDto>.Success(
            new InvitationPreviewDto(invitation.Email, companyName, inviterEmail ?? string.Empty));
    }
}
