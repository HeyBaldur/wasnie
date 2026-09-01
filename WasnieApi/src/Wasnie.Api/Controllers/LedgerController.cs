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

    // GET /api/payees/{payeeId}/ledger/summary?period=all-time
    //
    // Earnings AND debt in one answer. Exposed as a real endpoint rather than left as an in-process
    // query for the assistant alone, so the crossing can be exercised over the SAME pipeline everything
    // else is: authentication, permissions and the resource guard, in the order the attacker meets them.
    // NotFound on failure, for the indistinguishability reason above.
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        Guid payeeId, [FromQuery] string period = "all-time", CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(new GetPayeeLedgerSummaryQuery(payeeId, period), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    // GET /api/payees/{payeeId}/ledger/entries
    //
    // NotFound, not BadRequest: the only failure this query produces is "no such payee, or not yours",
    // and the two must be indistinguishable down to the status code (PayeeAccessDenied). A 403 here
    // would confirm the payee exists just as loudly as a different message would.
    [HttpGet("entries")]
    public async Task<IActionResult> ListEntries(Guid payeeId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListPayeeLedgerEntriesQuery(payeeId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    // GET /api/payees/ledger/terminated-with-balance — the work queue for finance: people who have
    // left with an account still open. Deliberately not under a payee id: it IS the list of which
    // payees to look at. Read permission, like the rest of the ledger reads.
    [HttpGet("/api/payees/ledger/terminated-with-balance")]
    public async Task<IActionResult> ListTerminatedWithBalance(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListTerminatedPayeesWithBalanceQuery(), cancellationToken);
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

    // POST /api/payees/{payeeId}/ledger/close-account
    //
    // ★ ITS OWN ENDPOINT, NOT AN ADJUSTMENT. An adjustment moves a balance; this ends an account —
    // marking a departed payee's unpaid commission terminal and zeroing any debt, in one transaction.
    // Own permission (Ledger.CloseAccount), own audit action, and no undo.
    //
    // ★★ AND IT CAN ANSWER 409. The body carries the exact credits the user was shown; if the account
    // has moved since, AccountSnapshotStaleException reaches the middleware and the client is told to
    // reload rather than to retry. See CloseTerminatedAccountHandler.
    [HttpPost("close-account")]
    public async Task<IActionResult> CloseAccount(
        Guid payeeId,
        [FromBody] CloseAccountRequest body,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<AccountClosureResolution>(body.Resolution, ignoreCase: true, out var resolution))
            return BadRequest(new { message = $"Unknown resolution '{body.Resolution}'." });

        // The payee comes from the route, like the adjustment above: the URL the caller was authorised
        // against is the account that gets closed.
        var result = await mediator.Send(
            new CloseTerminatedAccountCommand(
                payeeId,
                body.Currency,
                resolution,
                body.Note,
                body.Credits.Select(c => new ClosingCreditRef(c.CreditId, c.Amount)).ToList(),
                body.ExpectedBalance),
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

    /// <param name="Credits">
    /// Exactly the outstanding credits the modal displayed, with the amounts it displayed. The handler
    /// closes this set or none of it.
    /// </param>
    /// <param name="ExpectedBalance">
    /// The ledger balance the modal displayed. Null means it showed no balance row at all.
    /// </param>
    public sealed record CloseAccountRequest(
        string Currency,
        string Resolution,
        string Note,
        IReadOnlyList<CloseAccountCreditRef> Credits,
        decimal? ExpectedBalance);

    public sealed record CloseAccountCreditRef(Guid CreditId, decimal Amount);
}
