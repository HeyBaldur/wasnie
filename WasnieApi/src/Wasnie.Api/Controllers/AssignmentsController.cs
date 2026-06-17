using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Commands.Assignments;
using Wasnie.Application.Compensation.Queries.Assignments;
using System.Collections.Generic;

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

    [HttpPost("{assignmentId:guid}/activate")]
    public async Task<IActionResult> Activate(Guid assignmentId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ActivateAssignmentCommand(assignmentId), cancellationToken);
        return result.IsSuccess ? NoContent() : UnprocessableEntity(new { message = result.Error });
    }

    [HttpPost("bulk-activate")]
    public async Task<IActionResult> BulkActivate([FromBody] BulkAssignmentIdsRequest body, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new BulkActivateAssignmentsCommand(body.AssignmentIds), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("bulk-deactivate")]
    public async Task<IActionResult> BulkDeactivate([FromBody] BulkAssignmentIdsRequest body, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new BulkDeactivateAssignmentsCommand(body.AssignmentIds), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("bulk-delete")]
    public async Task<IActionResult> BulkDelete([FromBody] BulkAssignmentIdsRequest body, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new BulkDeleteAssignmentsCommand(body.AssignmentIds), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });
        // 200 always; frontend checks result.allDeleted
        return Ok(result.Value);
    }

    public record UpdateNotesRequest(string? Notes);
    public record BulkAssignmentIdsRequest(IReadOnlyList<Guid> AssignmentIds);
}
