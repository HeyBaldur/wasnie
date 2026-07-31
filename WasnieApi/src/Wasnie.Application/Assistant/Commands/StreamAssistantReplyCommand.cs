using MediatR;
using Wasnie.Application.Assistant.DTOs;

namespace Wasnie.Application.Assistant.Commands;

/// <summary>
/// One exchange, delivered as it happens: the user's turn, then the assistant's answer in fragments.
///
/// A <see cref="IStreamRequest{T}"/> rather than an ordinary command because the value of this feature
/// is temporal — the answer appearing as it is written. Collecting it and returning at the end would
/// be the same bytes and a worse product.
/// </summary>
public sealed record StreamAssistantReplyCommand(Guid ConversationId, string Content)
    : IStreamRequest<AssistantStreamEvent>;

/// <summary>
/// One frame of the exchange. A single shape with a discriminator rather than a hierarchy, because it
/// crosses the wire as JSON and the client switches on the type.
/// </summary>
/// <param name="Type">See the constants below.</param>
/// <param name="Delta">A fragment of the answer. Only on <see cref="Delta"/> frames.</param>
/// <param name="Message">A persisted row. On <see cref="UserTurn"/> and <see cref="Done"/>.</param>
/// <param name="ErrorKey">
/// A TRANSLATION KEY, never a sentence and never the provider's own words — the client renders it in
/// the reader's language, and a vendor error string can carry request ids or prompt fragments.
/// </param>
public sealed record AssistantStreamEvent(
    string Type,
    string? Delta = null,
    AssistantMessageDto? Message = null,
    string? ErrorKey = null)
{
    /// <summary>The user's turn, already persisted. Sent first so the client can replace its optimistic copy.</summary>
    public const string UserTurn = "user";

    /// <summary>A fragment of the answer.</summary>
    public const string Fragment = "delta";

    /// <summary>The answer finished and was persisted; carries the stored row.</summary>
    public const string Done = "done";

    /// <summary>The answer could not be produced. NOTHING was persisted for the assistant.</summary>
    public const string Error = "error";

    public static AssistantStreamEvent OfUser(AssistantMessageDto message) => new(UserTurn, Message: message);

    public static AssistantStreamEvent OfFragment(string delta) => new(Fragment, Delta: delta);

    public static AssistantStreamEvent OfDone(AssistantMessageDto message) => new(Done, Message: message);

    public static AssistantStreamEvent OfError(string errorKey) => new(Error, ErrorKey: errorKey);
}
