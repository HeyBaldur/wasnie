using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Users.Commands;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;
using Wasnie.Domain.Identity;

namespace Wasnie.Application.Features.Users.Handlers;

/// <summary>
/// Turns an invitation into a user (KAN-32). PUBLIC — nobody is signed in when this runs.
///
/// ★★ IT READS WITH IgnoreQueryFilters(), AND THAT IS THE ONLY WAY IT CAN WORK. Every other query in
/// this system is narrowed to the caller's tenant; here the caller has no tenant, because they have no
/// account. The TOKEN is the authorisation and the tenant is read OFF the row the token found — never
/// off anything the caller sent. A tenant id in the request would be the caller choosing which company
/// to join.
///
/// ★★ THE FOUR REFUSALS ARE DISTINGUISHED, EXCEPT THE ONE THAT MUST NOT BE. Used, expired and revoked
/// each tell the reader something they can act on. "No such token" and "malformed token" share one
/// answer, because telling them apart would turn this endpoint into an oracle for guessing tokens.
///
/// ★ THE EMAIL IS CONFIRMED ON ARRIVAL. The address already proved itself: the link only exists
/// because a message sent there was opened. Asking the person to confirm an address they just
/// demonstrated control of is a step that teaches them the product wastes their time.
/// </summary>
public sealed class AcceptInvitationHandler(
    IApplicationDbContext db,
    IIdentityService identityService,
    IAuditService auditService,
    IClock clock,
    IGuidGenerator guid,
    ILogger<AcceptInvitationHandler> logger)
    : IRequestHandler<AcceptInvitationCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        AcceptInvitationCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset;
        var tokenHash = InviteUserHandler.HashToken(request.Token);

        var invitation = await db.Invitations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);

        if (invitation is null)
            throw new DomainCodedException(InvitationRefusal.TokenInvalid);

        switch (invitation.StatusAt(now))
        {
            case InvitationStatus.Accepted:
                throw new DomainCodedException(InvitationRefusal.TokenAlreadyUsed);
            case InvitationStatus.Revoked:
                throw new DomainCodedException(InvitationRefusal.TokenRevoked);
            case InvitationStatus.Expired:
                throw new DomainCodedException(InvitationRefusal.TokenExpired);
        }

        // ★ THE ADDRESS IS RE-CHECKED AT THE LAST MOMENT. Days may have passed since the invitation
        // went out, and the same person may have been added another way in the meantime. Accepting
        // then would create a second account for one human being.
        // KAN-91. AN ADDRESS THAT ALREADY HAS AN ACCOUNT JOINS; IT DOES NOT SIGN UP AGAIN. One human
        // being is one set of credentials, and the invitation adds a MEMBERSHIP of this workspace to
        // the account they already have.
        //
        // THE PASSWORD THEY TYPED IS IGNORED ON THIS PATH, deliberately. Their existing one still
        // works, and quietly resetting it would mean anybody holding an invitation link could change
        // the password of an account that belongs to another company.
        var userId = await identityService.FindUserIdByEmailAsync(invitation.Email);

        if (userId is not null)
        {
            var alreadyHere = await db.TenantUsers
                .IgnoreQueryFilters()
                .AnyAsync(u => u.UserId == userId && u.TenantId == invitation.TenantId, cancellationToken);

            if (alreadyHere)
                throw new DomainCodedException(InvitationRefusal.EmailAlreadyMember,
                    new Dictionary<string, object?> { ["email"] = invitation.Email });
        }
        else
        {
            var (succeeded, createdId, errors) = await identityService.CreateUserAsync(
                invitation.Email,
                request.Password,
                [invitation.Role],
                new Dictionary<string, string>
                {
                    ["tenant_id"] = invitation.TenantId.ToString(),
                    ["given_name"] = request.FirstName.Trim(),
                    ["family_name"] = request.LastName.Trim(),
                });

            if (!succeeded || createdId is null)
            {
                // Identity's own complaints (password strength, duplicate address) are the one place a
                // sentence is still the honest answer: they come from a library we do not own.
                logger.LogWarning(
                    "Invitation {InvitationId} could not be accepted: {Errors}",
                    invitation.Id, string.Join("; ", errors));
                return Result<bool>.Failure(string.Join(" ", errors));
            }

            userId = createdId;
            await identityService.SetEmailConfirmedAsync(userId);
        }

        invitation.Accept(userId, now);

        db.TenantUsers.Add(TenantUser.Create(
            guid.NewGuid(),
            invitation.TenantId,
            userId,
            invitation.Role,
            invitation.Id,
            invitation.InvitedBy,
            now));

        // One SaveChanges: the invitation stops being usable and the access row appears together, or
        // neither does. There is no unit of work in this codebase — atomicity IS the single save.
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(new AuditEntry(
            TenantId: invitation.TenantId,
            Action: AuditActions.InvitationAccepted,
            ResourceType: "Invitation",
            ResourceId: invitation.Id.ToString(),
            // ★ THE ACTOR IS THE NEW USER, NOT THE INVITER. They are the one who acted; who invited
            // them is already recorded on the USER_INVITED entry made days earlier.
            ActorUserId: userId,
            ActorEmail: invitation.Email,
            DisplayName: invitation.Email,
            Metadata: new Dictionary<string, string> { ["role"] = invitation.Role }),
            cancellationToken);

        return Result<bool>.Success(true);
    }
}
