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
    // GET /api/dashboard?period=this-month
    [HttpGet]
    public async Task<IActionResult> GetSummary(
        [FromQuery] string period = "this-month",
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(new GetDashboardSummaryQuery(period), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }
}
