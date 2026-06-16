using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Compensation.Commands.PayRuns;
using Wasnie.Application.Compensation.Queries.PayRuns;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/pay-runs")]
[Authorize]
public sealed class PayRunsController(IMediator mediator) : ControllerBase
{
    // GET /api/pay-runs
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] PayRunFilterQuery filter, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListPayRunsQuery(filter), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    // GET /api/pay-runs/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromQuery] PayRunPayoutsFilter payoutsFilter,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetPayRunByIdQuery(id, payoutsFilter), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    // POST /api/pay-runs/calculate
    [HttpPost("calculate")]
    public async Task<IActionResult> Calculate(
        [FromBody] CalculatePayRunRequest body, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CalculatePayRunCommand(body.PeriodStart, body.PeriodEnd), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    // POST /api/pay-runs/{id}/approve
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ApprovePayRunCommand(id), cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(new { message = result.Error });
    }

    // POST /api/pay-runs/{id}/mark-paid
    [HttpPost("{id:guid}/mark-paid")]
    public async Task<IActionResult> MarkPaid(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new MarkPayRunPaidCommand(id), cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { message = result.Error });
        if (result.Value is not null) return Conflict(new { blocked = true, result.Value.TotalConflicts, conflicts = result.Value.Conflicts });
        return NoContent();
    }

    // POST /api/pay-runs/{id}/reopen
    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ReopenPayRunCommand(id), cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(new { message = result.Error });
    }

    // DELETE /api/pay-runs/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DeletePayRunDraftCommand(id), cancellationToken);
        if (!result.IsSuccess)
        {
            // 404 for not found, 409 for wrong-status (Approved/Paid — permanently locked)
            return result.Error!.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(new { message = result.Error })
                : Conflict(new { message = result.Error });
        }
        return NoContent();
    }

    // GET /api/pay-runs/{id}/overlaps
    [HttpGet("{id:guid}/overlaps")]
    public async Task<IActionResult> GetOverlaps(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetPayRunOverlapsQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    // GET /api/pay-runs/export
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] PayRunFilterQuery filter, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ExportPayRunsQuery(filter), cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.Error?.StartsWith("EXPORT_TOO_LARGE:", StringComparison.Ordinal) == true)
                return UnprocessableEntity(new { message = result.Error });
            return BadRequest(new { message = result.Error });
        }
        var export = result.Value!;
        return File(export.Bytes, export.ContentType, export.FileName);
    }
}

public sealed record CalculatePayRunRequest(DateOnly PeriodStart, DateOnly PeriodEnd);
