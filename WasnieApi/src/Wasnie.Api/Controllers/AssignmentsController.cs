using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Compensation.Commands.Assignments;
using Wasnie.Application.Compensation.Queries.Assignments;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/assignments")]
[Authorize]
public sealed class AssignmentsController(IMediator mediator) : ControllerBase
{
    [HttpGet("payee/{payeeId:guid}")]
    public async Task<IActionResult> ListByPayee(Guid payeeId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListAssignmentsByPayeeQuery(payeeId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("plan/{planId:guid}")]
    public async Task<IActionResult> ListByPlan(Guid planId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListPayeesByPlanQuery(planId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost]
    public async Task<IActionResult> Assign([FromBody] AssignPlanToPayeeCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("{assignmentId:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid assignmentId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DeactivateAssignmentCommand(assignmentId), cancellationToken);
        return result.IsSuccess ? NoContent() : UnprocessableEntity(new { message = result.Error });
    }
}
