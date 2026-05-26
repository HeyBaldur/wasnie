using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Commands.Payees;
using Wasnie.Application.Compensation.Queries.Assignments;
using Wasnie.Application.Compensation.Queries.Payees;
using Wasnie.Application.Compensation.Queries.Quotas;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/payees")]
[Authorize]
public sealed class PayeesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListPayeesQuery(pagination), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("{payeeId:guid}")]
    public async Task<IActionResult> Get(Guid payeeId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetPayeeByIdQuery(payeeId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePayeeCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { payeeId = result.Value!.Id }, result.Value)
            : BadRequest(new { message = result.Error });
    }

    [HttpPut("{payeeId:guid}")]
    public async Task<IActionResult> Update(Guid payeeId, [FromBody] UpdatePayeeCommand command, CancellationToken cancellationToken)
    {
        if (payeeId != command.PayeeId)
            return BadRequest(new { message = "Route payeeId does not match body." });

        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("{payeeId:guid}/mark-active")]
    public async Task<IActionResult> MarkActive(Guid payeeId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new MarkPayeeAsActiveCommand(payeeId), cancellationToken);
        return result.IsSuccess ? NoContent() : UnprocessableEntity(new { message = result.Error });
    }

    [HttpPost("{payeeId:guid}/mark-on-leave")]
    public async Task<IActionResult> MarkOnLeave(Guid payeeId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new MarkPayeeAsOnLeaveCommand(payeeId), cancellationToken);
        return result.IsSuccess ? NoContent() : UnprocessableEntity(new { message = result.Error });
    }

    [HttpPost("{payeeId:guid}/mark-terminated")]
    public async Task<IActionResult> MarkTerminated(Guid payeeId, [FromBody] TerminatePayeeRequest body, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new MarkPayeeAsTerminatedCommand(payeeId, body.TerminationDate), cancellationToken);
        return result.IsSuccess ? NoContent() : UnprocessableEntity(new { message = result.Error });
    }

    [HttpGet("{payeeId:guid}/assignments")]
    public async Task<IActionResult> ListAssignments(Guid payeeId, [FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListAssignmentsByPayeeQuery(payeeId, pagination), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("{payeeId:guid}/quotas")]
    public async Task<IActionResult> ListQuotas(Guid payeeId, [FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListQuotasByPayeeQuery(payeeId, pagination), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    public record TerminatePayeeRequest(DateOnly TerminationDate);
}
