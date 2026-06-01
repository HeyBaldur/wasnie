using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Settings.Commands;
using Wasnie.Application.Settings.Queries;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize]
public sealed class SettingsController(IMediator mediator) : ControllerBase
{
    [HttpGet("field-requirements")]
    public async Task<IActionResult> GetFieldRequirements(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetFieldRequirementsQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPut("field-requirements/{entityName}/{fieldName}")]
    public async Task<IActionResult> UpdateFieldRequirement(
        string entityName,
        string fieldName,
        [FromBody] UpdateFieldRequirementBody body,
        CancellationToken cancellationToken)
    {
        var command = new UpdateFieldRequirementCommand(entityName, fieldName, body.IsRequired);
        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(new { message = result.Error });
    }
}

public sealed record UpdateFieldRequirementBody(bool IsRequired);
