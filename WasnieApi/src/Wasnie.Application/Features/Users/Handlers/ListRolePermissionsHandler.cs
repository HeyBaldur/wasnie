using MediatR;
using Wasnie.Application.Authorization;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Users.Queries;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Users.Handlers;

/// <summary>
/// What each role can do, straight out of <see cref="RolePermissions"/>.
///
/// ★★ IT EXISTS SO THE SCREEN DOES NOT KEEP ITS OWN COPY. The users list had no way to say what
/// anybody could do — <c>/auth/me</c> answers only about the caller — so the alternative was a second
/// permission map living in the browser. That map would be right the day it was written and wrong the
/// first time somebody added a permission to a role in C#, and it would be wrong on the one screen
/// whose entire job is telling an administrator who can do what. A screen that lies about authority is
/// worse than a screen that says nothing.
///
/// ★★ IT RETURNS KEYS, NOT SENTENCES (§C1). "Payouts.Approve" is a code; the screen turns it into
/// words in the reader's language, from an explicit whitelist. A handler that emitted prose would need
/// a redeploy to fix a translation, and would ship English into a Polish workspace.
///
/// ★ USERS.READ, NOT USERS.MANAGE. This is the same question the list answers — who is here and what
/// are they — and reading it changes nothing. Anybody who may see the screen may understand it.
///
/// ★ NOTHING TENANT-SPECIFIC IS INVOLVED. The map is a constant of the product, identical in every
/// workspace, so there is nothing here to scope and nothing to leak: the answer for "Rep" is the same
/// answer whoever asks.
/// </summary>
public sealed class ListRolePermissionsHandler(IAuthorizationService authorizationService)
    : IRequestHandler<ListRolePermissionsQuery, Result<IReadOnlyList<RolePermissionsDto>>>
{
    public async Task<Result<IReadOnlyList<RolePermissionsDto>>> Handle(
        ListRolePermissionsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.UsersRead, cancellationToken);

        // ★★ EVERY ROLE THAT EXISTS, EACH FLAGGED WITH WHETHER IT MAY BE GRANTED. The access panel has to
        // describe a person who already holds a hidden role (CompManager, Manager) — dropping those
        // would paint them as "unknown" — while the pickers must offer only what the handlers accept.
        // One answer serves both, and the pickers keep no list of their own: reactivating a role is a
        // change to Roles.Assignable and nothing else.
        //
        // ★ ORDER COMES FROM Roles.All, not from the permission map, whose key order is a dictionary's
        // and not a product decision.
        var roles = Roles.All
            .Select(role => new RolePermissionsDto(
                Role: role,
                Permissions: RolePermissions.GetPermissions(role).OrderBy(p => p, StringComparer.Ordinal).ToList(),
                Assignable: Roles.IsAssignable(role)))
            .ToList();

        return Result<IReadOnlyList<RolePermissionsDto>>.Success(roles);
    }
}
