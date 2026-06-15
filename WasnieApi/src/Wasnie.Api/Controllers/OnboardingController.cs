using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Features.Onboarding.Commands;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/onboarding")]
[Authorize]
public sealed class OnboardingController(IMediator mediator) : ControllerBase
{
    [HttpPost("qualify")]
    public async Task<IActionResult> Qualify(
        [FromBody] CompleteQualificationCommand command,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return Ok(new { message = "Qualification complete." });
    }
}
