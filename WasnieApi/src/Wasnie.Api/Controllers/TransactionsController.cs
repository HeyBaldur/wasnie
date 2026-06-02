using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Commands.Transactions;
using Wasnie.Application.Compensation.Queries.Transactions;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/transactions")]
[Authorize]
public sealed class TransactionsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListTransactionsQuery(pagination), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTransactionByIdQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpPost]
    public async Task<IActionResult> Ingest([FromBody] IngestTransactionCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess
            ? Created($"api/transactions/{result.Value!.Id}", result.Value)
            : BadRequest(new { message = result.Error });
    }

    [HttpPost("{id:guid}/assign-payee")]
    public async Task<IActionResult> AssignPayee(Guid id, [FromBody] AssignPayeeRequest body, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new AssignPayeeCommand(id, body.PayeeId, body.Comment), cancellationToken);
        if (!result.IsSuccess)
        {
            var isDomainBlock = result.Error?.Contains("Paid", StringComparison.OrdinalIgnoreCase) == true
                                || result.Error?.Contains("already has", StringComparison.OrdinalIgnoreCase) == true;
            return isDomainBlock
                ? Conflict(new { message = result.Error })
                : BadRequest(new { message = result.Error });
        }
        return Ok(result.Value);
    }

    [HttpPost("{id:guid}/reassign-payee")]
    public async Task<IActionResult> ReassignPayee(Guid id, [FromBody] ReassignPayeeRequest body, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ReassignPayeeCommand(id, body.NewPayeeId, body.Reason), cancellationToken);
        if (!result.IsSuccess)
        {
            var isDomainBlock = result.Error?.Contains("Paid", StringComparison.OrdinalIgnoreCase) == true
                                || result.Error?.Contains("no assigned payee", StringComparison.OrdinalIgnoreCase) == true;
            return isDomainBlock
                ? Conflict(new { message = result.Error })
                : BadRequest(new { message = result.Error });
        }
        return Ok(result.Value);
    }

    public record AssignPayeeRequest(Guid PayeeId, string? Comment);
    public record ReassignPayeeRequest(Guid NewPayeeId, string Reason);
}
