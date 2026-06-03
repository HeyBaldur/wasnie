using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Commands.Assignments;
using Wasnie.Application.Compensation.Queries.Assignments;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/assignments")]
[Authorize]
public sealed class AssignmentsController(IMediator mediator) : ControllerBase
{
    [HttpGet("{assignmentId:guid}")]
    public async Task<IActionResult> GetById(Guid assignmentId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetAssignmentByIdQuery(assignmentId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListAssignmentsQuery(pagination), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("payee/{payeeId:guid}")]
    public async Task<IActionResult> ListByPayee(Guid payeeId, [FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListAssignmentsByPayeeQuery(payeeId, pagination), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("plan/{planId:guid}")]
    public async Task<IActionResult> ListByPlan(Guid planId, [FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListPayeesByPlanQuery(planId, pagination), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost]
    public async Task<IActionResult> Assign([FromBody] AssignPlanToPayeeCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPut("{assignmentId:guid}/notes")]
    public async Task<IActionResult> UpdateNotes(Guid assignmentId, [FromBody] UpdateNotesRequest body, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new UpdateAssignmentNotesCommand(assignmentId, body.Notes), cancellationToken);
        return result.IsSuccess ? NoContent() : UnprocessableEntity(new { message = result.Error });
    }

    [HttpPost("{assignmentId:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid assignmentId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DeactivateAssignmentCommand(assignmentId), cancellationToken);
        return result.IsSuccess ? NoContent() : UnprocessableEntity(new { message = result.Error });
    }

    public record UpdateNotesRequest(string? Notes);
}
