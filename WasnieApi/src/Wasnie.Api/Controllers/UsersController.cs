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
    /// Its own request type rather than the command, so the user id comes from the ROUTE and cannot
    /// be overridden by the body — §D3, and here it is also an authorisation boundary.
    /// </summary>
    public sealed record ChangeUserRoleRequest(string Role);
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
