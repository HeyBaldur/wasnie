using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Assistant.Commands;
using Wasnie.Application.Assistant.Queries;

namespace Wasnie.Api.Controllers;

/// <summary>
/// The assistant's chat.
///
/// The panel uses the STREAMING endpoint (`/messages/stream`); the plain `/messages` one is the same
/// exchange collected before it returns, for callers that cannot hold a stream open. Both answer
/// through the same provider and the same prompt.
///
/// Access is an ENTITLEMENT checked inside each handler, not an [Authorize(Roles=...)] attribute here.
/// That is deliberate: the day a seat is sold to a non-admin, the change must be one edit inside
/// AssistantEntitlement, not an attribute hunt across this file.
/// </summary>
[ApiController]
[Route("api/assistant")]
[Authorize]
public sealed class AssistantController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// "Do I get the assistant?" — answerable by any authenticated user, returns a plain boolean.
    /// A user without a seat gets `{enabled:false}`, not a 403: they did not do anything wrong.
    /// </summary>
    [HttpGet("entitlement")]
    public async Task<IActionResult> Entitlement(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetAssistantEntitlementQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    /// <summary>
    /// One batch of the caller's conversations, newest activity first, plus the cursor for the next.
    ///
    /// ★ THE SEARCH RIDES ON THIS ROUTE rather than getting one of its own, so the filtered list pages
    /// exactly like the unfiltered one — same order, same cursor, same response shape, one code path in
    /// the client. See <see cref="ListConversationsQuery"/>.
    /// </summary>
    [HttpGet("conversations")]
    public async Task<IActionResult> ListConversations(
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new ListConversationsQuery(cursor, pageSize, search), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    // 404 rather than 403 when the conversation is not the caller's — see OwnedConversations. A 403
    // would confirm that someone else's conversation exists at that id.
    [HttpGet("conversations/{conversationId:guid}")]
    public async Task<IActionResult> GetConversation(Guid conversationId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetConversationQuery(conversationId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpPost("conversations")]
    public async Task<IActionResult> StartConversation(
        [FromBody] StartConversationCommand? command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command ?? new StartConversationCommand(), cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetConversation), new { conversationId = result.Value!.Id }, result.Value)
            : BadRequest(new { message = result.Error });
    }

    [HttpPost("conversations/{conversationId:guid}/messages")]
    public async Task<IActionResult> PostMessage(
        Guid conversationId, [FromBody] PostMessageBody body, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new PostMessageCommand(conversationId, body.Content), cancellationToken);

        if (result.IsSuccess)
            return Ok(result.Value);

        // "Not found" is an ownership answer, not a bad request.
        return result.Error == Application.Assistant.Common.OwnedConversations.NotFound
            ? NotFound(new { message = result.Error })
            : BadRequest(new { message = result.Error });
    }

    /// <summary>
    /// The connected exchange, streamed as Server-Sent Events.
    ///
    /// SSE rather than WebSockets: the traffic is one-way and short-lived, it rides the existing HTTP
    /// stack with the existing auth, and it needs no connection lifecycle of its own. A socket would be
    /// infrastructure bought for a feature that never talks back.
    ///
    /// Each frame is one `data:` line of JSON — `user`, `delta`, `done`, `error`. The API KEY IS NEVER
    /// PART OF ANY OF THEM: the browser talks to Wasnie, Wasnie talks to the model.
    /// </summary>
    [HttpPost("conversations/{conversationId:guid}/messages/stream")]
    public async Task StreamMessage(
        Guid conversationId, [FromBody] PostMessageBody body, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        // Tells nginx and friends not to buffer — buffering would collect the whole answer and deliver
        // it at once, which is precisely the wait this endpoint exists to remove.
        Response.Headers["X-Accel-Buffering"] = "no";

        var stream = mediator.CreateStream(
            new StreamAssistantReplyCommand(conversationId, body.Content, body.IsRetry), cancellationToken);

        await foreach (var frame in stream.WithCancellation(cancellationToken))
        {
            var json = JsonSerializer.Serialize(frame, StreamJsonOptions);
            await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    private static readonly JsonSerializerOptions StreamJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [HttpPut("conversations/{conversationId:guid}")]
    public async Task<IActionResult> RenameConversation(
        Guid conversationId, [FromBody] RenameConversationBody body, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new RenameConversationCommand(conversationId, body.Title), cancellationToken);

        if (result.IsSuccess)
            return Ok(result.Value);

        return result.Error == Application.Assistant.Common.OwnedConversations.NotFound
            ? NotFound(new { message = result.Error })
            : BadRequest(new { message = result.Error });
    }

    /// <summary>
    /// Pins a conversation for the CALLER. Idempotent.
    ///
    /// ★ 404 RATHER THAN 403 when it is not the caller's, exactly as the read does — a 403 would confirm
    /// that somebody else's conversation exists at that id. 422 is kept for the one refusal that is
    /// about the request rather than the record: the pin limit, which comes back as a translation key
    /// the client renders in the reader's own language.
    /// </summary>
    [HttpPost("conversations/{conversationId:guid}/pin")]
    public async Task<IActionResult> PinConversation(Guid conversationId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new PinConversationCommand(conversationId), cancellationToken);

        if (result.IsSuccess)
            return NoContent();

        return result.Error == Application.Assistant.Common.OwnedConversations.NotFound
            ? NotFound(new { message = result.Error })
            : UnprocessableEntity(new { messageKey = result.Error });
    }

    [HttpDelete("conversations/{conversationId:guid}/pin")]
    public async Task<IActionResult> UnpinConversation(Guid conversationId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new UnpinConversationCommand(conversationId), cancellationToken);

        return result.IsSuccess
            ? NoContent()
            : NotFound(new { message = result.Error });
    }

    [HttpDelete("conversations/{conversationId:guid}")]
    public async Task<IActionResult> DeleteConversation(Guid conversationId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DeleteConversationCommand(conversationId), cancellationToken);
        return result.IsSuccess ? NoContent() : NotFound(new { message = result.Error });
    }

    /// <summary>Body-only shape: the conversation id comes from the route, so it is not repeated here.</summary>
    /// <param name="IsRetry">
    /// Set by the Retry button after a failed answer. The question is already stored, so the server
    /// re-answers the last user turn instead of writing it a second time — see the command.
    /// Defaulted, so the non-streaming endpoint and every existing caller are unaffected.
    /// </param>
    public sealed record PostMessageBody(string Content, bool IsRetry = false);

    public sealed record RenameConversationBody(string Title);
}
