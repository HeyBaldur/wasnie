using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Compensation.Queries.Dashboard;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController(IMediator mediator) : ControllerBase
{
    // GET /api/dashboard?from=2026-02-01&to=2026-04-15
    // Both omitted → the whole current month (the handler decides; see GetDashboardSummaryQuery).
    [HttpGet]
    public async Task<IActionResult> GetSummary(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(new GetDashboardSummaryQuery(from, to), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }
}
