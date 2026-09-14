using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Features.UiPreferences;

namespace Wasnie.Api.Controllers;

/// <summary>
/// The signed-in user's UI preferences (KAN-78). Generic by design: one read of everything, one write per key.
/// Always scoped to the caller — there is no route that names another user.
/// </summary>
[ApiController]
[Route("api/me/ui-preferences")]
[Authorize]
public sealed class UiPreferencesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetUiPreferencesQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPut("{key}")]
    public async Task<IActionResult> Set(string key, [FromBody] SetUiPreferenceRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new SetUiPreferenceCommand(key, request.Value), cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(new { message = result.Error });
    }
}

public sealed record SetUiPreferenceRequest(string Value);
