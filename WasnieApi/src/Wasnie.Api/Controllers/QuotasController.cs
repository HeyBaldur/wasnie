using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Compensation.Commands.Quotas;
using Wasnie.Application.Compensation.Queries.Quotas;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/quotas")]
[Authorize]
public sealed class QuotasController(IMediator mediator) : ControllerBase
{
    [HttpGet("{quotaId:guid}")]
    public async Task<IActionResult> Get(Guid quotaId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetQuotaByIdQuery(quotaId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpGet("payee/{payeeId:guid}")]
    public async Task<IActionResult> ListByPayee(Guid payeeId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListQuotasByPayeeQuery(payeeId), cancellationToken);
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
