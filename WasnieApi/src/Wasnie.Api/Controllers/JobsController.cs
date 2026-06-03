using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.BackgroundJobs.Queries;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/jobs")]
[Authorize]
public sealed class JobsController(IMediator mediator, IBackgroundJobService jobService, ITenantContext tenantContext) : ControllerBase
{
    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> GetStatus(Guid jobId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetJobStatusQuery(jobId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpPost("{jobId:guid}/cancel")]
    public async Task<IActionResult> CancelJob(Guid jobId, CancellationToken cancellationToken)
    {
        var cancelled = await jobService.CancelJobAsync(jobId, tenantContext.TenantId, cancellationToken);
        return cancelled ? NoContent() : NotFound(new { message = "Job not found or already in a terminal state." });
    }
}
