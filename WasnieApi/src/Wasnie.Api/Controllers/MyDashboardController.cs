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
    /// <summary>
    /// Their balance, what they have earned, and how their quotas went.
    ///
    /// GET /api/me/dashboard?from=2026-07-01&amp;to=2026-07-31
    ///
    /// ★★ THE WINDOW IS THE ONLY INPUT, AND IT IS NOT AN IDENTIFIER (KAN-98). Two dates say WHICH DAYS,
    /// never WHOSE: the payee is still resolved from the token and from nothing else, so the security
    /// property of this route — that there is no version of the request that reads somebody else's pay —
    /// is exactly what it was before the parameters existed.
    ///
    /// ★ BOTH OMITTED → no window at all, which is what this endpoint answered before the range. It is
    /// not "the current month": the client chooses the month it opens on, and a server that quietly
    /// picked one would make an unparameterised call mean something different from one day to the next.
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(new GetMyDashboardQuery(from, to), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(result.Value);
    }
}
