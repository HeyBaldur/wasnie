using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Payouts;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Application.Models.Calculation;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/payouts")]
[Authorize]
public sealed class PayoutsController(
    IMediator mediator,
    IBackgroundJobService jobService,
    ITenantContext tenantContext,
    ICurrentUserService currentUser) : ControllerBase
{
    // GET /api/payouts
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] PayoutFilterQuery filter, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListPayoutsQuery(filter), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    // GET /api/payouts/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetPayoutByIdQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    // POST /api/payouts/calculate  — enqueues a Hangfire job, returns 202 with jobId
    [HttpPost("calculate")]
    public async Task<IActionResult> Calculate(
        [FromBody] CalculatePayoutsRequest body, CancellationToken cancellationToken)
    {
        var payload = new CalculatePayoutsPayload(
            TenantId: tenantContext.TenantId,
            PeriodStart: body.PeriodStart,
            PeriodEnd: body.PeriodEnd,
            PayeeIdFilter: body.PayeeIdFilter,
            TriggeredBy: currentUser.UserId ?? "system",
            TriggeredByEmail: currentUser.Email ?? string.Empty);

        var jobId = await jobService.EnqueueAsync(
            payload,
            tenantContext.TenantId,
            currentUser.UserId ?? "system",
            currentUser.Email ?? string.Empty,
            cancellationToken);

        return StatusCode(202, new { jobId });
    }

    // POST /api/payouts/{id}/approve
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ApprovePayoutCommand(id), cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(new { message = result.Error });
    }

    // POST /api/payouts/{id}/mark-paid
    [HttpPost("{id:guid}/mark-paid")]
    public async Task<IActionResult> MarkPaid(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new MarkPayoutPaidCommand(id), cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(new { message = result.Error });
    }

    // POST /api/payouts/bulk-approve
    [HttpPost("bulk-approve")]
    public async Task<IActionResult> BulkApprove(
        [FromBody] BulkApproveRequest body, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new BulkApprovePayoutsCommand(body.PayoutIds), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    // POST /api/payouts/bulk-mark-paid
    [HttpPost("bulk-mark-paid")]
    public async Task<IActionResult> BulkMarkPaid(
        [FromBody] BulkMarkPaidRequest body, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new BulkMarkPaidCommand(body.PayoutIds), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    // GET /api/payouts/{id}/overlaps
    [HttpGet("{id:guid}/overlaps")]
    public async Task<IActionResult> GetOverlaps(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetPayoutOverlapsQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    // POST /api/payouts/overlaps-check
    [HttpPost("overlaps-check")]
    public async Task<IActionResult> CheckOverlaps(
        [FromBody] OverlapsCheckRequest body, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new CheckPayoutsOverlapsQuery(body.PayoutIds), cancellationToken);
        return result.IsSuccess ? Ok(new { count = result.Value }) : BadRequest(new { message = result.Error });
    }

    // GET /api/payouts/export
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] PayoutFilterQuery filter, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ExportPayoutsQuery(filter), cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.Error?.StartsWith("EXPORT_TOO_LARGE:", StringComparison.Ordinal) == true)
                return UnprocessableEntity(new { message = result.Error });
            return BadRequest(new { message = result.Error });
        }
        var export = result.Value!;
        return File(export.Bytes, export.ContentType, export.FileName);
    }

    // GET /api/payouts/{id}/export/pdf
    [HttpGet("{id:guid}/export/pdf")]
    public async Task<IActionResult> ExportPdf(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ExportPayoutPdfQuery(id), cancellationToken);
        if (!result.IsSuccess)
            return NotFound(new { message = result.Error });

        var export = result.Value!;
        return File(export.Bytes, export.ContentType, export.FileName);
    }
}

public sealed record CalculatePayoutsRequest(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    Guid? PayeeIdFilter = null);

public sealed record BulkApproveRequest(IReadOnlyList<Guid> PayoutIds);
public sealed record BulkMarkPaidRequest(IReadOnlyList<Guid> PayoutIds);
public sealed record OverlapsCheckRequest(IReadOnlyList<Guid> PayoutIds);
