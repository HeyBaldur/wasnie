using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Compensation.Queries.Plans;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/plans")]
[Authorize]
public sealed class PlansController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListPlansQuery(pagination), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("{planId:guid}")]
    public async Task<IActionResult> Get(Guid planId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetPlanByIdQuery(planId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpGet("versions/{planName}")]
    public async Task<IActionResult> Versions(string planName, [FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListPlanVersionsQuery(planName, pagination), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    /// <summary>
    /// Transaction attributes a rule trigger can filter on, with the operators each one honours.
    /// The rule builder's field picker is driven by this so it can never offer a field or operator
    /// the engine ignores.
    /// </summary>
    [HttpGet("trigger-fields")]
    public async Task<IActionResult> TriggerFields(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTriggerFieldsQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    /// <summary>
    /// The distinct category values that exist for this tenant. Feeds the rule builder's value picker
    /// for a condition on the <c>category</c> field, so the value is chosen from reality rather than
    /// typed (a typo would save cleanly and then never match).
    /// </summary>
    [HttpGet("category-values")]
    public async Task<IActionResult> CategoryValues(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetCategoryValuesQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("{planId:guid}/multi-plan-payees")]
    public async Task<IActionResult> MultiPlanPayees(Guid planId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetMultiPlanPayeesQuery(planId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePlanCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { planId = result.Value!.Id }, result.Value)
            : BadRequest(new { message = result.Error });
    }

    [HttpPost("{planId:guid}/clone")]
    public async Task<IActionResult> Clone(Guid planId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ClonePlanVersionCommand(planId), cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { planId = result.Value!.Id }, result.Value)
            : BadRequest(new { message = result.Error });
    }

    /// <summary>
    /// Turns the clawback on for this plan. Sending both fields null turns it off again — a plan
    /// with no maturation window claws nothing back, which is where every plan starts.
    /// </summary>
    [HttpPut("{planId:guid}/clawback-policy")]
    public async Task<IActionResult> SetClawbackPolicy(
        Guid planId,
        [FromBody] SetClawbackPolicyRequest body,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new SetPlanClawbackPolicyCommand(planId, body.MaturationDays, body.CapPercent),
            cancellationToken);
        return result.IsSuccess ? NoContent() : UnprocessableEntity(new { message = result.Error });
    }

    public sealed record SetClawbackPolicyRequest(int? MaturationDays, decimal? CapPercent);

    [HttpPost("{planId:guid}/activate")]
    public async Task<IActionResult> Activate(Guid planId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ActivatePlanCommand(planId), cancellationToken);
        return result.IsSuccess ? NoContent() : UnprocessableEntity(new { message = result.Error });
    }

    [HttpPost("{planId:guid}/archive")]
    public async Task<IActionResult> Archive(Guid planId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ArchivePlanCommand(planId), cancellationToken);
        return result.IsSuccess ? NoContent() : UnprocessableEntity(new { message = result.Error });
    }

    [HttpDelete("{planId:guid}")]
    public async Task<IActionResult> Delete(Guid planId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DeletePlanCommand(planId), cancellationToken);
        return result.IsSuccess ? NoContent() : UnprocessableEntity(new { message = result.Error });
    }
}
