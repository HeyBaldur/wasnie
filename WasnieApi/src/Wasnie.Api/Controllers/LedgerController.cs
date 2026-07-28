using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Compensation.Commands.Ledger;
using Wasnie.Application.Compensation.Queries.Ledger;

namespace Wasnie.Api.Controllers;

/// <summary>
/// The payee's clawback account: what they are owed, what was withheld, and every entry that moved
/// the balance. Read is open to anyone with Ledger.Read (including the rep themselves — seeing why
/// a payment shrank is the point); writing an adjustment needs Ledger.Adjust.
/// </summary>
[ApiController]
[Route("api/payees/{payeeId:guid}/ledger")]
[Authorize]
public sealed class LedgerController(IMediator mediator) : ControllerBase
{
    // GET /api/payees/{payeeId}/ledger/statement — one statement per currency, all figures final.
    [HttpGet("statement")]
    public async Task<IActionResult> GetStatement(Guid payeeId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetPayeeStatementQuery(payeeId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    // GET /api/payees/{payeeId}/ledger/entries
    [HttpGet("entries")]
    public async Task<IActionResult> ListEntries(Guid payeeId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListPayeeLedgerEntriesQuery(payeeId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    // POST /api/payees/{payeeId}/ledger/adjustments
    [HttpPost("adjustments")]
    public async Task<IActionResult> CreateAdjustment(
        Guid payeeId,
        [FromBody] CreateAdjustmentRequest body,
        CancellationToken cancellationToken)
    {
        // The payee comes from the route, not the body: the URL a user was authorised against is
        // the one the entry lands on.
        var result = await mediator.Send(
            new CreateManualLedgerAdjustmentCommand(
                payeeId, body.TransactionType, body.Amount, body.Currency, body.Justification),
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(new { message = result.Error });
    }

    public sealed record CreateAdjustmentRequest(
        string TransactionType,
        decimal Amount,
        string Currency,
        string Justification);
}
