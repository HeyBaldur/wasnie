using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wasnie.Application.Features.Users.Commands;
using Wasnie.Application.Features.Users.Queries;

namespace Wasnie.Api.Controllers;

/// <summary>
/// Who has access to this tenant, and the invitations that have not been taken up yet (KAN-32).
///
/// ★ THE PERMISSION CHECK IS IN THE HANDLERS, NOT ON THE ACTIONS. [Authorize] here only says a
/// session is required; Users.Read and Users.Manage are enforced by IAuthorizationService inside each
/// handler, as every other feature in this codebase does. Putting a policy on the action as well would
/// create a second place to keep in step with RolePermissions.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController(IMediator mediator) : ControllerBase
{
    /// <summary>People, outstanding invitations and the seat position, in one response.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListTenantUsersQuery(), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(result.Value);
    }

    /// <summary>
    /// What each assignable role can do — the data behind the access panel on the users screen.
    ///
    /// ★★ IT IS SERVED SO THE BROWSER DOES NOT KEEP ITS OWN COPY OF THE PERMISSION MAP. There was no
    /// way for this screen to say what anybody could do, because /auth/me answers only about the
    /// caller; the alternative was a second map in TypeScript, correct the day it was written and
    /// wrong the first time a permission moved in C# — on the one screen whose job is telling an
    /// administrator who can do what.
    ///
    /// ★ The map is a constant of the product, identical in every workspace, so the answer carries
    /// nothing tenant-specific. Keys, never sentences: the screen owns the words.
    /// </summary>
    [HttpGet("roles")]
    public async Task<IActionResult> Roles(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListRolePermissionsQuery(), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(result.Value);
    }

    /// <summary>
    /// The payees nobody owns yet, for the two pickers that attach one (KAN-92, KAN-93).
    ///
    /// ★ TWO INDEPENDENT INPUTS, AND THEY MUST NOT BE FOLDED INTO ONE. <c>search</c> is what the
    /// administrator is typing into the dropdown; <c>email</c> is the invitee's address, used only to
    /// point at a likely match. Answering the second from the first would make the suggestion vanish
    /// as soon as somebody starts typing a name.
    /// </summary>
    [HttpGet("unlinked-payees")]
    public async Task<IActionResult> UnlinkedPayees(
        [FromQuery] string? email,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListUnlinkedPayeesQuery(email, search), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(result.Value);
    }

    [HttpPost("invitations")]
    public async Task<IActionResult> Invite(
        [FromBody] InviteUserCommand command,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(result.Value);
    }

    [HttpPost("invitations/{id:guid}/resend")]
    public async Task<IActionResult> Resend(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ResendInvitationCommand(id), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(result.Value);
    }

    [HttpDelete("invitations/{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RevokeInvitationCommand(id), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return NoContent();
    }

    [HttpPost("{userId}/deactivate")]
    public async Task<IActionResult> Deactivate(string userId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DeactivateUserCommand(userId), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return NoContent();
    }

    [HttpPost("{userId}/reactivate")]
    public async Task<IActionResult> Reactivate(string userId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ReactivateUserCommand(userId), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return NoContent();
    }

    /// <summary>
    /// Ends somebody's membership of this workspace. DELETE, because that is what it does — the row
    /// goes. Deactivating, which keeps them listed, is the POST above.
    /// </summary>
    [HttpDelete("{userId}")]
    public async Task<IActionResult> Remove(string userId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RemoveUserCommand(userId), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return NoContent();
    }

    [HttpPut("{userId}/role")]
    public async Task<IActionResult> ChangeRole(
        string userId,
        [FromBody] ChangeUserRoleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ChangeUserRoleCommand(userId, request.Role), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return NoContent();
    }

    /// <summary>
    /// Attaches this login to a payee record, or detaches it when <c>payeeId</c> is null (KAN-93).
    ///
    /// ★ PUT, NOT POST, AND ONE ROUTE FOR BOTH DIRECTIONS. The link is a single-valued property of
    /// the user — they are one payee or none — so setting it is idempotent and naming its absence
    /// null is the honest spelling. A separate DELETE would be a second route that has to agree with
    /// this one about what "no payee" means.
    /// </summary>
    [HttpPut("{userId}/payee")]
    public async Task<IActionResult> LinkPayee(
        string userId,
        [FromBody] LinkUserPayeeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new LinkUserToPayeeCommand(userId, request.PayeeId), cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return NoContent();
    }

    /// <summary>
    /// Its own request type rather than the command, so the user id comes from the ROUTE and cannot
    /// be overridden by the body — §D3, and here it is also an authorisation boundary.
    /// </summary>
    public sealed record ChangeUserRoleRequest(string Role);

    /// <summary>Same reason as above: the user id is the route's, never the body's.</summary>
    public sealed record LinkUserPayeeRequest(Guid? PayeeId);
}

/// <summary>
/// The two routes somebody without an account has to be able to reach (KAN-32).
///
/// ★★ A SEPARATE CONTROLLER, SO [AllowAnonymous] IS NOT A HOLE PUNCHED IN AN AUTHORISED ONE. Mixing
/// anonymous actions into UsersController would mean every future action added there starts one
/// forgotten attribute away from being public. Here the whole class is public and says so.
///
/// ★ RATE LIMITED LIKE THE OTHER TOKEN ROUTES. A public endpoint that answers "is this token real"
/// is guessable in principle; the limiter is what makes it not worth trying.
/// </summary>
[ApiController]
[Route("api/invitations")]
[AllowAnonymous]
public sealed class InvitationsController(IMediator mediator) : ControllerBase
{
    /// <summary>What the accept page shows: the company, the inviter and the address invited.</summary>
    [HttpGet("{token}")]
    [EnableRateLimiting("invitations")]
    public async Task<IActionResult> Preview(string token, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetInvitationByTokenQuery(token), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(result.Value);
    }

    [HttpPost("accept")]
    [EnableRateLimiting("invitations")]
    public async Task<IActionResult> Accept(
        [FromBody] AcceptInvitationCommand command,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(new { message = "Invitation accepted." });
    }
}
