using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Subscription.Commands;
using Wasnie.Application.Features.Subscription.Queries;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/subscription")]
[Authorize]
public sealed class SubscriptionController(
    IMediator mediator,
    IOptions<StripeOptions> stripeOptions,
    IStripeWebhookService webhookService)
    : ControllerBase
{
    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetSubscriptionPlansQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("select-free")]
    public async Task<IActionResult> SelectFree(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new SelectFreePlanCommand(), cancellationToken);
        return result.IsSuccess ? Ok() : BadRequest(new { message = result.Error });
    }

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetCurrentSubscriptionQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        // The publishable key is safe to expose to the frontend — it is designed
        // to be public and is required by Stripe.js for client-side tokenisation.
        // The secret key is never returned from any endpoint.
        return Ok(new { publishableKey = stripeOptions.Value.PublishableKey });
    }

    [HttpPost("checkout")]
    public async Task<IActionResult> CreateCheckout(
        [FromBody] CreateCheckoutSessionCommand command,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    // No [Authorize] — called by Stripe, not by a user.
    // Security is the webhook signature verification inside webhookService.ProcessAsync.
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        // Must read the raw body before any binding so the Stripe signature can be verified.
        using var reader = new StreamReader(HttpContext.Request.Body);
        var json = await reader.ReadToEndAsync(cancellationToken);

        var stripeSignature = Request.Headers["Stripe-Signature"].FirstOrDefault() ?? string.Empty;

        var result = await webhookService.ProcessAsync(json, stripeSignature, cancellationToken);

        // Always respond quickly to Stripe — they retry on non-2xx responses.
        return result.IsSuccess ? Ok() : BadRequest(new { message = result.Error });
    }
}
