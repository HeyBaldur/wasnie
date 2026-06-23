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

    [HttpGet("pending-count")]
    public async Task<IActionResult> PendingCount([FromQuery] PendingCountRequest req, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetPendingTransactionsCountQuery(req.Scope, req.ScopeId, req.PeriodStart, req.PeriodEnd),
            cancellationToken);
        return result.IsSuccess ? Ok(new { count = result.Value }) : BadRequest(new { message = result.Error });
    }

    [HttpGet("eligible-pending")]
    public async Task<IActionResult> EligiblePending([FromQuery] PendingCountRequest req, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetEligiblePendingTransactionsQuery(req.Scope, req.ScopeId, req.PeriodStart, req.PeriodEnd),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("{id:guid}/void")]
    public async Task<IActionResult> Void(Guid id, [FromBody] VoidTransactionRequest body, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new VoidTransactionCommand(id, body.Reason), cancellationToken);
        if (!result.IsSuccess)
        {
            var isDomainBlock = result.Error?.Contains("Only Pending", StringComparison.OrdinalIgnoreCase) == true
                                || result.Error?.Contains("already cancelled", StringComparison.OrdinalIgnoreCase) == true
                                || result.Error?.Contains("active credits", StringComparison.OrdinalIgnoreCase) == true;
            return isDomainBlock
                ? Conflict(new { message = result.Error })
                : BadRequest(new { message = result.Error });
        }
        return Ok(result.Value);
    }

    /// <summary>Void (cancel) many transactions at once. Returns the voided count + per-transaction errors.</summary>
    [HttpPost("bulk-void")]
    public async Task<IActionResult> BulkVoid([FromBody] BulkVoidRequest body, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new BulkVoidTransactionsCommand(body.TransactionIds, body.Reason), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("process-pending")]
    public async Task<IActionResult> ProcessPending([FromBody] ProcessPendingTransactionsCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess
            ? Accepted(new { jobId = result.Value!.JobId, candidateCount = result.Value.CandidateCount })
            : BadRequest(new { message = result.Error });
    }

    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] PaginationQuery filter, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ExportTransactionsQuery(filter), cancellationToken);
        if (!result.IsSuccess)
        {
            // Propagate "too large" as 422 so the frontend can prompt user confirmation.
            if (result.Error?.StartsWith("EXPORT_TOO_LARGE:", StringComparison.Ordinal) == true)
                return UnprocessableEntity(new { message = result.Error });
            return BadRequest(new { message = result.Error });
        }
        var export = result.Value!;
        return File(export.Bytes, export.ContentType, export.FileName);
    }

    public record AssignPayeeRequest(Guid PayeeId, string? Comment);
    public record ReassignPayeeRequest(Guid NewPayeeId, string Reason);
    public record VoidTransactionRequest(string Reason);
    public record BulkVoidRequest(IReadOnlyList<Guid> TransactionIds, string Reason);
    public record PendingCountRequest(
        Wasnie.Application.Compensation.Calculation.ProcessPendingScope Scope,
        Guid? ScopeId,
        DateOnly? PeriodStart,
        DateOnly? PeriodEnd);
}
