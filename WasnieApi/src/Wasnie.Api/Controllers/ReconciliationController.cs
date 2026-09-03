using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Compensation.Queries.Reconciliation;

namespace Wasnie.Api.Controllers;

/// <summary>
/// The Reconciliation Centre: every piece of earned money the system could not turn into a payment,
/// with its reason. Read-only in v1 — see, filter, export.
/// </summary>
[ApiController]
[Authorize]
[Route("api/reconciliation")]
public sealed class ReconciliationController(ISender mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] Guid? payeeId,
        [FromQuery] string? reason,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new GetReconciliationQuery(new ReconciliationFilter(payeeId, reason, from, to, page, pageSize)),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    /// <summary>
    /// The vocabulary of reasons, so the filter offers exactly what the queue can contain.
    ///
    /// ★ SERVED, NOT HARD-CODED IN THE CLIENT. A reason added to the engine appears in the filter
    /// without a front-end release; the front still whitelists what it can TRANSLATE, which is a
    /// different question from what it can filter by.
    /// </summary>
    [HttpGet("reasons")]
    public IActionResult Reasons() => Ok(ReconciliationReason.All);

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] Guid? payeeId,
        [FromQuery] string? reason,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        // Page and size are deliberately not accepted: an export is the whole filtered set.
        var result = await mediator.Send(
            new ExportReconciliationQuery(new ReconciliationFilter(payeeId, reason, from, to)),
            cancellationToken);

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
