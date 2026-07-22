using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Compensation.Queries.Plans;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/plans")]
[Authorize]
public sealed class PlansController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListPlansQuery(pagination), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("{planId:guid}")]
    public async Task<IActionResult> Get(Guid planId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetPlanByIdQuery(planId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpGet("versions/{planName}")]
    public async Task<IActionResult> Versions(string planName, [FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListPlanVersionsQuery(planName, pagination), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("{planId:guid}/multi-plan-payees")]
    public async Task<IActionResult> MultiPlanPayees(Guid planId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetMultiPlanPayeesQuery(planId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePlanCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { planId = result.Value!.Id }, result.Value)
            : BadRequest(new { message = result.Error });
    }

    [HttpPost("{planId:guid}/clone")]
    public async Task<IActionResult> Clone(Guid planId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ClonePlanVersionCommand(planId), cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { planId = result.Value!.Id }, result.Value)
            : BadRequest(new { message = result.Error });
    }

    [HttpPost("{planId:guid}/activate")]
    public async Task<IActionResult> Activate(Guid planId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ActivatePlanCommand(planId), cancellationToken);
        return result.IsSuccess ? NoContent() : UnprocessableEntity(new { message = result.Error });
    }

    [HttpPost("{planId:guid}/archive")]
    public async Task<IActionResult> Archive(Guid planId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ArchivePlanCommand(planId), cancellationToken);
        return result.IsSuccess ? NoContent() : UnprocessableEntity(new { message = result.Error });
    }

    [HttpDelete("{planId:guid}")]
    public async Task<IActionResult> Delete(Guid planId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DeletePlanCommand(planId), cancellationToken);
        return result.IsSuccess ? NoContent() : UnprocessableEntity(new { message = result.Error });
    }
}
