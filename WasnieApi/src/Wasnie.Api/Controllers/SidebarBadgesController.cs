using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Compensation.Queries.Sidebar;

namespace Wasnie.Api.Controllers;

/// <summary>
/// The counts the sidebar shows beside its links.
///
/// ★ ONE ROUTE FOR ALL OF THEM. Three endpoints would mean three round trips on every page load to
/// draw three small numbers, each repeating the same authorisation and tenant resolution.
/// </summary>
[ApiController]
[Authorize]
[Route("api/sidebar-badges")]
public sealed class SidebarBadgesController(ISender mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetSidebarBadgesQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }
}
