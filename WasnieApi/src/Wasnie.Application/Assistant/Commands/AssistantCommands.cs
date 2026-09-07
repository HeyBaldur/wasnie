using MediatR;
using Wasnie.Application.Assistant.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Assistant.Commands;

/// <summary>
/// Starts an empty conversation. The title is optional: without one the handler names it, because a
/// thread that exists with no name is a row the history list cannot render.
/// </summary>
public sealed record StartConversationCommand(string? Title = null)
    : IRequest<Result<AssistantConversationDto>>;

/// <summary>
/// One user turn, and the assistant's stand-in reply.
///
/// ★ NO MODEL IS CALLED. The reply is a stored sentinel that the UI renders in the user's language.
/// When a model is wired in (a later piece), the shape of this command does not change — only what
/// fills the assistant message does.
/// </summary>
public sealed record PostMessageCommand(Guid ConversationId, string Content)
    : IRequest<Result<AssistantExchangeDto>>;

public sealed record RenameConversationCommand(Guid ConversationId, string Title)
    : IRequest<Result<AssistantConversationSummaryDto>>;

public sealed record DeleteConversationCommand(Guid ConversationId) : IRequest<Result>;

/// <summary>
/// Pins a conversation FOR THE CALLER. Idempotent: pinning something already pinned changes nothing,
/// and in particular does NOT move it to the top of the pinned group — see AssistantConversationState.
///
/// ★ THERE IS NO USER PARAMETER, AND THAT IS THE AUTHORISATION. A pin belongs to whoever asked for it;
/// a command that could name a user would be a command that could pin something in somebody else's
/// list. The caller is taken from the principal, never from the request.
/// </summary>
public sealed record PinConversationCommand(Guid ConversationId) : IRequest<Result>;

/// <summary>Unpins it for the caller. Idempotent, and the standing row survives — see the entity.</summary>
public sealed record UnpinConversationCommand(Guid ConversationId) : IRequest<Result>;

/// <summary>
/// Records what the user did with a clarify form (KAN-58).
///
/// ★ THE MESSAGE IS PART OF THE KEY, NOT JUST THE CONVERSATION. A thread can hold several forms over
/// its life, and "close the clarify form" without saying which one would close whichever the server
/// found first.
/// </summary>
/// <param name="State">
/// <c>answered</c> or <c>dismissed</c> — see ClarifyState. <c>open</c> is deliberately not accepted:
/// a form must not be reopened by a client after the person closed it.
/// </param>
public sealed record ResolveClarifyCommand(Guid ConversationId, Guid MessageId, string State)
    : IRequest<Result>;
