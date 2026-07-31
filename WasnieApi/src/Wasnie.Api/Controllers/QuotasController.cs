using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Commands.Quotas;
using Wasnie.Application.Compensation.Queries.Quotas;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/quotas")]
[Authorize]
public sealed class QuotasController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListQuotasQuery(pagination), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("{quotaId:guid}")]
    public async Task<IActionResult> Get(Guid quotaId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetQuotaByIdQuery(quotaId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error! });
    }

    [HttpGet("payee/{payeeId:guid}")]
    public async Task<IActionResult> ListByPayee(Guid payeeId, [FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListQuotasByPayeeQuery(payeeId, pagination), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateQuotaCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { quotaId = result.Value!.Id }, result.Value)
            : BadRequest(new { message = result.Error });
    }

    // POST /api/quotas/bulk — one quota configuration for N payees, all-or-nothing.
    // 400 carries the per-payee failure list, and in that case NOTHING was written: the handler
    // refuses the batch before it reaches SaveChanges. Never a partial success.
    [HttpPost("bulk")]
    public async Task<IActionResult> BulkCreate(
        [FromBody] BulkCreateQuotasCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return result.Value!.IsSuccess
            ? Ok(result.Value)
            : BadRequest(result.Value);
    }

    [HttpPut("{quotaId:guid}")]
    public async Task<IActionResult> Update(Guid quotaId, [FromBody] UpdateQuotaCommand command, CancellationToken cancellationToken)
    {
        if (quotaId != command.QuotaId)
            return BadRequest(new { message = "Route quotaId does not match body." });

        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : UnprocessableEntity(new { message = result.Error });
    }

    [HttpPost("{quotaId:guid}/activate")]
    public async Task<IActionResult> Activate(Guid quotaId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ActivateQuotaCommand(quotaId), cancellationToken);
        return result.IsSuccess ? NoContent() : UnprocessableEntity(new { message = result.Error });
    }

    [HttpPost("{quotaId:guid}/close")]
    public async Task<IActionResult> Close(Guid quotaId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new CloseQuotaCommand(quotaId), cancellationToken);
        return result.IsSuccess ? NoContent() : UnprocessableEntity(new { message = result.Error });
    }
}
