using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Features.Users.Queries;

namespace Wasnie.Api.Controllers;

/// <summary>
/// The signed-in person's own screen (KAN-92).
///
/// ★★ THE ROUTE TAKES NO IDENTIFIER, AND THAT IS THE SECURITY PROPERTY. Everything under /api/me is
/// about whoever holds the token; there is no path segment to change, so there is no version of this
/// request that reads somebody else's pay. The neighbouring ledger endpoints DO take a payee id and
/// need PayeeAccessGuard to decide whether the caller may have it — this one removes the question.
///
/// ★ THE PERMISSION CHECK IS IN THE HANDLER, as everywhere else in this codebase. [Authorize] here
/// says only that a session is required.
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
public sealed class MyDashboardController(IMediator mediator) : ControllerBase
{
    /// <summary>Their balance, what they have earned, and how their live quotas are going.</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetMyDashboardQuery(), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(result.Value);
    }
}
