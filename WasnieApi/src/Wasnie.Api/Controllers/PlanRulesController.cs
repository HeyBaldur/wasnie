using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Compensation.Commands.Plans;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/plans/{planId:guid}/rules")]
[Authorize]
public sealed class PlanRulesController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Add(
        Guid planId,
        [FromBody] AddRuleToPlanCommand command,
        CancellationToken cancellationToken)
    {
        if (command.PlanId != planId)
        {
            return BadRequest(new { message = "Route planId does not match body planId." });
        }

        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : UnprocessableEntity(new { message = result.Error });
    }

    [HttpPut("{ruleId:guid}")]
    public async Task<IActionResult> Update(
        Guid planId,
        Guid ruleId,
        [FromBody] UpdateRuleCommand command,
        CancellationToken cancellationToken)
    {
        if (command.PlanId != planId || command.RuleId != ruleId)
        {
            return BadRequest(new { message = "Route identifiers do not match body identifiers." });
        }

        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : UnprocessableEntity(new { message = result.Error });
    }

    [HttpDelete("{ruleId:guid}")]
    public async Task<IActionResult> Remove(Guid planId, Guid ruleId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RemoveRuleFromPlanCommand(planId, ruleId), cancellationToken);
        return result.IsSuccess ? NoContent() : UnprocessableEntity(new { message = result.Error });
    }
}
