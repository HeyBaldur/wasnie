using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Integrations.HubSpot;

namespace Wasnie.Api.Controllers;

/// <summary>
/// HubSpot OAuth integration (Phase 1). Tokens are stored encrypted and are NEVER returned by any endpoint.
/// </summary>
[ApiController]
[Route("api/integrations/hubspot")]
[Authorize]
public sealed class IntegrationsController(
    IMediator mediator,
    IOptions<HubSpotOptions> hubSpotOptions)
    : ControllerBase
{
    private readonly HubSpotOptions _opts = hubSpotOptions.Value;

    /// <summary>Current connection status for the tenant (safe fields only — no tokens).</summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetHubSpotStatusQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    /// <summary>Starts the OAuth flow; returns the HubSpot authorization URL for the browser to navigate to.</summary>
    [HttpPost("connect")]
    public async Task<IActionResult> Connect(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new StartHubSpotConnectionCommand(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    /// <summary>
    /// OAuth callback hit by HubSpot via a top-level browser redirect (no JWT → anonymous). Exchanges the
    /// code, persists the encrypted connection, then redirects the browser back to the integrations UI.
    /// </summary>
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        // User denied access (or HubSpot returned an error) — bounce back with an error flag.
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return Redirect(ReturnUrl("error"));

        var result = await mediator.Send(new HandleHubSpotCallbackCommand(code, state), cancellationToken);
        return Redirect(ReturnUrl(result.IsSuccess ? "connected" : "error"));
    }

    /// <summary>User-initiated disconnect.</summary>
    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DisconnectHubSpotCommand(), cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(new { message = result.Error });
    }

    /// <summary>Verification-only: makes one trivial authenticated HubSpot call to confirm the connection works.</summary>
    [HttpPost("ping")]
    public async Task<IActionResult> Ping(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new PingHubSpotQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    private string ReturnUrl(string status)
    {
        var baseUrl = _opts.FrontendReturnUrl;
        var separator = baseUrl.Contains('?') ? "&" : "?";
        return $"{baseUrl}{separator}hubspot={status}";
    }
}
