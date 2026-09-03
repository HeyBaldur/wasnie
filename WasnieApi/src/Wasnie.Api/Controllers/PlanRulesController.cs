using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Compensation.Queries.Plans;

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

    /// <summary>
    /// What one hypothetical transaction would earn under a rule, step by step.
    ///
    /// ★ IT TAKES THE RULE'S DEFINITION, NOT ITS ID — so the screen can simulate what is on the form
    /// right now, including a rule that has never been saved. POST rather than GET because that
    /// definition is a whole object, not a query string; nothing is created and nothing is written.
    /// </summary>
    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate(
        Guid planId,
        [FromBody] SimulateRuleQuery query,
        CancellationToken cancellationToken)
    {
        if (query.PlanId != planId)
        {
            return BadRequest(new { message = "Route planId does not match body planId." });
        }

        var result = await mediator.Send(query, cancellationToken);
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

    /// <summary>
    /// THE EMERGENCY BRAKE. Stop one rule of a live plan from generating further credit.
    ///
    /// ★ POST, NOT DELETE, AND THE DIFFERENCE IS THE POINT. DELETE on this route already means
    /// "remove a rule from a draft that was never activated" — a different act with a different
    /// guard. This one records a fact on a plan that is paying people right now, and it is
    /// irreversible: there is no matching endpoint to undo it.
    /// </summary>
    [HttpPost("{ruleId:guid}/stop")]
    public async Task<IActionResult> Stop(
        Guid planId,
        Guid ruleId,
        [FromBody] StopRuleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new StopRuleCommand(planId, ruleId, request?.Reason), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : UnprocessableEntity(new { message = result.Error });
    }

    /// <summary>
    /// The body of a stop request. ★ A REQUEST TYPE OF ITS OWN, not the command — the ids come from
    /// the route, so binding the command directly would let a body override the plan being braked.
    /// </summary>
    public sealed record StopRuleRequest(string? Reason);

    [HttpDelete("{ruleId:guid}")]
    public async Task<IActionResult> Remove(Guid planId, Guid ruleId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RemoveRuleFromPlanCommand(planId, ruleId), cancellationToken);
        return result.IsSuccess ? NoContent() : UnprocessableEntity(new { message = result.Error });
    }
}
