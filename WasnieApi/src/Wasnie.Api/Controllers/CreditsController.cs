using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Compensation.Queries.Credits;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/credits")]
[Authorize]
public sealed class CreditsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] CreditFilterQuery filter, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListCreditsQuery(filter), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("counters")]
    public async Task<IActionResult> Counters([FromQuery] CreditFilterQuery filter, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetCreditCountersQuery(filter), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("by-payee")]
    public async Task<IActionResult> ByPayee([FromQuery] CreditFilterQuery filter, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetCreditsByPayeeQuery(filter), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetCreditByIdQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }
}
