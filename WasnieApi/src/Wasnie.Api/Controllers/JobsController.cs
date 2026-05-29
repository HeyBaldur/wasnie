using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.BackgroundJobs.Queries;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/jobs")]
[Authorize]
public sealed class JobsController(IMediator mediator) : ControllerBase
{
    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> GetStatus(Guid jobId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetJobStatusQuery(jobId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }
}
