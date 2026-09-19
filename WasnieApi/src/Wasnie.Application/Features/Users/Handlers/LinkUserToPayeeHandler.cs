using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Users.Commands;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Exceptions;
using Wasnie.Domain.Identity;

namespace Wasnie.Application.Features.Users.Handlers;

/// <summary>
/// Attaches a login to a payee record outside the invitation flow (KAN-93).
///
/// ★★ THE HOLE IT CLOSES WAS PERMANENT, NOT MERELY INCONVENIENT. Until now <c>Payee.UserId</c> was
/// written in exactly one place — <c>AcceptInvitationHandler</c> — so the link could only be made in
/// the seconds somebody accepted their invitation. Anybody whose account already existed when their
/// payee record was created was stranded: their personal dashboard correctly said "ask an
/// administrator to link it", and the administrator had no way to do it. Re-inviting is refused
/// because the address is already a member. There was no third option.
///
/// ★★ IT RE-ASKS EVERY QUESTION <c>AcceptInvitationHandler</c> ASKS, because it is the same act done
/// later by a different person: the payee must exist in THIS workspace, and it must not already belong
/// to another login. Re-pointing a live link is refused rather than performed, for the reason
/// <see cref="InvitationRefusal.PayeeAlreadyLinked"/> gives — moving it moves who may read somebody's
/// pay, so it is done deliberately, by detaching first.
///
/// ★★ THE TARGET MUST BE A MEMBER OF THIS WORKSPACE. Without that check an administrator could name
/// any user id in the system and hand a stranger from another tenant a window onto this tenant's
/// money. The membership table is tenant-filtered, so the check is the query.
///
/// ★ DETACHING IS DELIBERATELY EASY AND ATTACHING IS NOT. Unlinking narrows what somebody may see —
/// it fails closed — so it takes no confirmation beyond the permission. That asymmetry is the same one
/// <see cref="Payee.UnlinkFromUser"/> already documents.
/// </summary>
public sealed class LinkUserToPayeeHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IAuditService auditService,
    IClock clock)
    : IRequestHandler<LinkUserToPayeeCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        LinkUserToPayeeCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.UsersManage, cancellationToken);

        var isMember = await db.TenantUsers
            .AnyAsync(u => u.UserId == request.UserId, cancellationToken);

        if (!isMember)
            return Result<bool>.Failure("User not found in this tenant.");

        var now = clock.UtcNowOffset;
        var actor = currentUser.UserId ?? string.Empty;

        // Whatever this login is attached to today. There is at most one: the link is a single column
        // on the payee, so "which payee is this person" has one answer by construction.
        var current = await db.Payees
            .FirstOrDefaultAsync(p => p.UserId == request.UserId, cancellationToken);

        // ★ NOTHING TO DO IS A SUCCESS, NOT A WRITE. Re-sending the same link from a stale screen must
        // not produce an audit entry saying somebody changed something they did not.
        if (current?.Id == request.PayeeId)
            return Result<bool>.Success(true);

        Payee? target = null;

        if (request.PayeeId is Guid payeeId)
        {
            target = await db.Payees.FirstOrDefaultAsync(p => p.Id == payeeId, cancellationToken)
                ?? throw new DomainCodedException(InvitationRefusal.PayeeNotFound);

            if (target.UserId is not null && target.UserId != request.UserId)
                throw new DomainCodedException(InvitationRefusal.PayeeAlreadyLinked);
        }

        // Detach first, always: a person moving from one payee record to another must not be readable
        // as both for the instant in between, and the old row must stop pointing at them regardless.
        current?.UnlinkFromUser(actor, now);
        target?.LinkToUser(request.UserId, actor, now);

        // One save — the detach and the attach land together or neither does. There is no unit of work
        // in this codebase; atomicity IS the single SaveChanges.
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(new AuditEntry(
            TenantId: tenantContext.TenantId,
            Action: AuditActions.UserPayeeLinkChanged,
            ResourceType: "User",
            ResourceId: request.UserId,
            ActorUserId: actor,
            ActorEmail: currentUser.Email ?? string.Empty,
            DisplayName: await identityService.FindEmailByUserIdAsync(request.UserId) ?? request.UserId,
            // Both sides carry the NAME as well as the id: an entry that says only
            // "00000000-… → 11111111-…" cannot answer the question anybody asks of it afterwards.
            Before: new { PayeeId = current?.Id, PayeeName = current?.FullName },
            After: new { PayeeId = target?.Id, PayeeName = target?.FullName }),
            cancellationToken);

        return Result<bool>.Success(true);
    }
}
