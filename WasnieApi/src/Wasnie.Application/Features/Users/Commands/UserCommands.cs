using MediatR;
using Wasnie.Application.Features.Users.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Users.Commands;

/// <summary>
/// Invites an email address to this tenant with a role. Requires Users.Manage.
///
/// PayeeId is OPTIONAL and says which payee record this person is (KAN-92). Null for somebody who
/// does not get paid — an administrator, a finance user. It is never inferred from the address.
/// </summary>
public sealed record InviteUserCommand(string Email, string Role, Guid? PayeeId = null)
    : IRequest<Result<InvitationDto>>;

/// <summary>Sends the invitation again with a NEW token; the previous link stops working.</summary>
public sealed record ResendInvitationCommand(Guid InvitationId) : IRequest<Result<InvitationDto>>;

/// <summary>Withdraws an outstanding invitation. Its seat is released.</summary>
public sealed record RevokeInvitationCommand(Guid InvitationId) : IRequest<Result<bool>>;

/// <summary>
/// Turns an invitation into a user. PUBLIC — the caller has no session, and the token is the
/// authorisation.
/// </summary>
public sealed record AcceptInvitationCommand(
    string Token,
    string FirstName,
    string LastName,
    string Password) : IRequest<Result<bool>>;

/// <summary>
/// Closes somebody's access without deleting anything (§B6). Their audit trail stays intact and
/// referenceable, and their seat is released.
/// </summary>
public sealed record DeactivateUserCommand(string UserId) : IRequest<Result<bool>>;

/// <summary>Re-opens access. Does NOT erase the fact that it was ever closed.</summary>
public sealed record ReactivateUserCommand(string UserId) : IRequest<Result<bool>>;

/// <summary>
/// Ends somebody's membership of THIS workspace (KAN-91).
///
/// ★★ NOT THE SAME AS DEACTIVATING, and the screen must not treat them as two words for one thing.
/// Deactivate closes access and keeps the person on the list, greyed, so an admin can re-open it.
/// Remove ends the membership: they stop belonging here, stop appearing, and stop counting. Their
/// ACCOUNT, their sign-in and every audit entry with their name on it survive untouched — which is
/// what KAN-32's "never delete" protects. A membership is a grant, and revoking a grant is not
/// erasing a person.
/// </summary>
public sealed record RemoveUserCommand(string UserId) : IRequest<Result<bool>>;

/// <summary>Moves somebody to a different role, replacing the old one rather than adding to it.</summary>
public sealed record ChangeUserRoleCommand(string UserId, string Role) : IRequest<Result<bool>>;

/// <summary>
/// Attaches an existing login to a payee record, or detaches it (KAN-93).
///
/// ★★ IT EXISTS BECAUSE INVITING WAS THE ONLY WAY TO LINK, AND YOU CANNOT INVITE SOMEBODY TWICE.
/// KAN-92 put <c>PayeeId</c> on the invitation and wrote the link on acceptance, which covers exactly
/// one moment in a person's life here. Anybody who already had an account when their payee record was
/// created — every user of every existing tenant — had no path at all: their personal dashboard told
/// them to ask an administrator, and the administrator had no button to press. Re-inviting is refused
/// (<c>INVITATION_EMAIL_ALREADY_MEMBER</c>), so the state was permanent.
///
/// ★★ <c>PayeeId</c> NULL MEANS DETACH, and it is a deliberate part of the same command rather than a
/// second one. Re-pointing a link is detach-then-attach whichever way it is expressed, and splitting
/// it would let a caller do half of it.
///
/// ★ THE USER ID COMES FROM THE ROUTE, NOT THE BODY (§D3). It is an authorisation boundary: a body
/// that could name a different user would let an administrator's own request be rewritten in flight.
/// </summary>
public sealed record LinkUserToPayeeCommand(string UserId, Guid? PayeeId) : IRequest<Result<bool>>;
