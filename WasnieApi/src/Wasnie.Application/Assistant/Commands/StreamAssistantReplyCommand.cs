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
/// <param name="IsRetry">
/// True when the user pressed Retry after a failed answer.
///
/// ★ WHY A FLAG AND NOT A SECOND COMMAND. On a retry the user's turn is ALREADY STORED — the backend
/// commits it before it calls the model precisely so a provider failure cannot lose the question. So a
/// retry must NOT write it again; re-sending the text would put the same question in the thread twice
/// and the user would watch their own message duplicate as the reward for pressing the button. The
/// flag skips exactly that one step and re-runs everything after it, which keeps retrying and asking
/// on ONE code path — two paths would eventually answer differently.
///
/// <see cref="Content"/> is ignored when this is set: the question is read from the stored thread,
/// which is the only version of it that is authoritative.
/// </param>
public sealed record StreamAssistantReplyCommand(Guid ConversationId, string Content, bool IsRetry = false)
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
/// <param name="Phase">
/// Which step of the turn this frame is about. Only on <see cref="Progress"/> frames. An identifier from
/// <see cref="AssistantPhase"/>, NOT a sentence: the client owns the wording and translates it, exactly
/// as it does for <paramref name="ErrorKey"/>.
/// </param>
/// <param name="State">
/// <see cref="PhaseStart"/> or <see cref="PhaseDone"/>. Only on <see cref="Progress"/> frames.
/// </param>
/// <param name="Title">
/// The conversation's title as it stands after this turn was committed. Only on
/// <see cref="UserTurn"/> frames.
///
/// ★★ IT RIDES WITH THE USER'S TURN BECAUSE THAT IS WHEN IT IS DECIDED. The thread takes its name from
/// the first thing said in it, and that happens in the SAME SaveChanges as the message — before the
/// model is called. The client had no way to learn it: the `user` frame carried the message and nothing
/// else, so the open conversation's header went on saying "New conversation" while the history list
/// picked the title up later, on its own refresh. One fact, two arrival times, and the user watching
/// both.
///
/// Sent even when the title did not change (a second message, or a thread the user renamed): "what the
/// title is now" is always true and needs no branch, while "the title changed" would need the server to
/// track what this client already knows.
/// </param>
public sealed record AssistantStreamEvent(
    string Type,
    string? Delta = null,
    AssistantMessageDto? Message = null,
    string? ErrorKey = null,
    string? Phase = null,
    string? State = null,
    string? Title = null)
{
    /// <summary>The user's turn, already persisted. Sent first so the client can replace its optimistic copy.</summary>
    public const string UserTurn = "user";

    /// <summary>A fragment of the answer.</summary>
    public const string Fragment = "delta";

    /// <summary>The answer finished and was persisted; carries the stored row.</summary>
    public const string Done = "done";

    /// <summary>The answer could not be produced. NOTHING was persisted for the assistant.</summary>
    public const string Error = "error";

    /// <summary>
    /// A step of the turn started or finished. Carries <see cref="Phase"/> and <see cref="State"/>.
    ///
    /// ★ PURELY ADDITIVE, and that is a requirement rather than a nicety. A client that has never heard
    /// of this frame must keep working: it carries no answer, no stored row and no failure, so ignoring
    /// it loses nothing — the existing switch simply falls through. The old panel and the new one can be
    /// served by the same backend.
    /// </summary>
    public const string Progress = "progress";

    /// <summary>The step began.</summary>
    public const string PhaseStart = "start";

    /// <summary>The step finished. A step that FAILED never gets one — the error frame ends the turn.</summary>
    public const string PhaseDone = "done";

    public static AssistantStreamEvent OfUser(AssistantMessageDto message, string title) =>
        new(UserTurn, Message: message, Title: title);

    public static AssistantStreamEvent OfFragment(string delta) => new(Fragment, Delta: delta);

    public static AssistantStreamEvent OfDone(AssistantMessageDto message) => new(Done, Message: message);

    public static AssistantStreamEvent OfError(string errorKey) => new(Error, ErrorKey: errorKey);

    public static AssistantStreamEvent OfPhaseStart(string phase) =>
        new(Progress, Phase: phase, State: PhaseStart);

    public static AssistantStreamEvent OfPhaseDone(string phase) =>
        new(Progress, Phase: phase, State: PhaseDone);
}

/// <summary>
/// The steps a turn is made of — the ones that REALLY happen, which is the whole point of this list.
///
/// ★ THE BACKEND REPORTS, IT DOES NOT NARRATE. Every one of these names a piece of work the handler
/// actually performs, and it is emitted only on the turns where that work is performed. A question the
/// documentation answers emits no <see cref="SearchingData"/>, because nothing was searched; a question
/// nothing in the guide covers emits no <see cref="ReadingDocs"/>, because nothing was read. The
/// sequence is therefore DIFFERENT from turn to turn, and that difference is the information.
///
/// This is the same rule the panel's old waiting text obeyed by saying nothing specific: never claim a
/// stage that cannot be verified. What changed is not the rule — it is that the backend, unlike the
/// browser, actually knows.
/// </summary>
public static class AssistantPhase
{
    /// <summary>
    /// Working out what the question needs: which parts of the guide, and whether a lookup is required.
    ///
    /// Covers BOTH classifier calls — the section router and the lookup dispatcher — because they are
    /// one thing to the person waiting, and splitting them would show two steps for a decision they
    /// never asked to watch being made.
    /// </summary>
    public const string Understanding = "understanding";

    /// <summary>Pulling the routed sections of the product guide into the prompt.</summary>
    public const string ReadingDocs = "reading_docs";

    /// <summary>Reading this tenant's own records, through the read-only tool the dispatcher chose.</summary>
    public const string SearchingData = "searching_data";

    /// <summary>Writing the answer.</summary>
    public const string Generating = "generating";
}
