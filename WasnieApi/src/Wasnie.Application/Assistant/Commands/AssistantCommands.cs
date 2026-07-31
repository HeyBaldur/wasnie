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
