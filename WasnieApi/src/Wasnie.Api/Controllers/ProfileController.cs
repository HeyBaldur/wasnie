using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Application.Features.Auth.Queries;
using Wasnie.Application.Features.Profile.Commands;
using Wasnie.Application.Features.Profile.Queries;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize]
public sealed class ProfileController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetProfileQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpPut("name")]
    public async Task<IActionResult> UpdateName(
        [FromBody] UpdateProfileNameCommand command,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(new { message = "Name updated successfully." });
    }

    [HttpPost("change-password")]
    [EnableRateLimiting("profile-password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordCommand command,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(new { message = "Password changed. Please sign in again." });
    }

    [HttpPost("request-email-change")]
    [EnableRateLimiting("profile-email-change")]
    public async Task<IActionResult> RequestEmailChange(
        [FromBody] RequestEmailChangeCommand command,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(new { message = "Confirmation email sent. Please check your new inbox." });
    }

    [HttpGet("confirm-email-change")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmailChange(
        [FromQuery] string userId,
        [FromQuery] string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
            return BadRequest(new { message = "Invalid email change link." });

        var result = await mediator.Send(new ConfirmEmailChangeCommand(userId, token), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(new { message = "Email address updated successfully. Please sign in with your new email." });
    }

    // ── Two-Factor Authentication ───────────────────────────────────────────

    [HttpGet("2fa/status")]
    public async Task<IActionResult> GetTwoFactorStatus(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTwoFactorStatusQuery(), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(result.Value);
    }

    [HttpGet("2fa/setup")]
    [EnableRateLimiting("profile-2fa")]
    public async Task<IActionResult> GetTwoFactorSetup(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTwoFactorSetupQuery(), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(result.Value);
    }

    [HttpPost("2fa/enable")]
    [EnableRateLimiting("profile-2fa")]
    public async Task<IActionResult> EnableTwoFactor(
        [FromBody] EnableTwoFactorCommand command,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(result.Value);
    }

    [HttpPost("2fa/disable")]
    [EnableRateLimiting("profile-2fa")]
    public async Task<IActionResult> DisableTwoFactor(
        [FromBody] DisableTwoFactorCommand command,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(new { message = "Two-factor authentication has been disabled." });
    }

    [HttpPost("2fa/recovery-codes")]
    [EnableRateLimiting("profile-2fa")]
    public async Task<IActionResult> RegenerateRecoveryCodes(
        [FromBody] RegenerateRecoveryCodesCommand command,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(new { recoveryCodes = result.Value });
    }
}
