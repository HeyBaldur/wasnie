using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Audit.DTOs;
using Wasnie.Application.Audit.Queries;

namespace Wasnie.Api.Controllers;

/// <summary>
/// The tenant's audit trail (KAN-19): who did what, when, to which entity.
///
/// ★ EVERY ACTION IS GUARDED INSIDE ITS HANDLER, by Permission.AuditRead — not by an attribute here.
/// That is the app's convention and it is the safer one: a handler reached from anywhere else (a
/// test, a future endpoint, an internal caller) carries its own guard with it.
///
/// ★ READ-ONLY, AND STRUCTURALLY SO. There is no write verb on this controller and there must never
/// be one: an audit trail that the application can edit is not evidence.
/// </summary>
[ApiController]
[Authorize]
[Route("api/audit-logs")]
public sealed class AuditLogsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? action,
        [FromQuery] string? actor,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new GetAuditLogsQuery(new AuditLogFilter(action, actor, from, to, page, pageSize)),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    /// <summary>The vocabulary of the two dropdowns — see GetAuditLogFilterOptionsHandler.</summary>
    [HttpGet("options")]
    public async Task<IActionResult> Options(CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(new GetAuditLogFilterOptionsQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    /// <summary>
    /// The evidence behind one row.
    ///
    /// ★ NotFound, NOT BadRequest, when the row is not the caller's. The tenant filter turns another
    /// tenant's id into a miss, and "not found" is the only answer that does not confirm the row
    /// exists somewhere.
    /// </summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detail(long id, CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(new GetAuditLogDetailQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }
}
