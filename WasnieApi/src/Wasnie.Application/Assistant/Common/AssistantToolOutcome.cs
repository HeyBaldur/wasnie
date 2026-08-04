namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// What step 1.5 actually did — which is three different things that used to be one empty string.
///
/// ★ THE BUG THIS TYPE EXISTS TO END. The runner returned `string.Empty` from SIX different paths: no
/// tools registered, the model choosing no tool, the provider rejecting the model's generated call, an
/// unknown tool name, the provider being unreachable, and the lookup itself throwing. The caller could
/// not tell them apart, so a TECHNICAL FAILURE arrived at the generating call as "no data" — and the
/// model, asked about a specific transaction with nothing in hand, answered that it could not find it.
///
/// That is worse than a missing answer. The record exists and the user can see it on screen; the
/// assistant asserted a fact about a row it never queried. The user is told "no" by something that
/// never asked the question — and there is no way to tell that turn from a genuine, correct refusal.
///
/// So the three outcomes are now distinct:
///
///   - <see cref="NotAttempted"/> — nothing was looked up, and nothing should have been. The ordinary
///     case for a documentation question. Answer normally.
///   - <see cref="Completed"/> — the lookup ran. Its payload may say the record was not found or is not
///     visible; that is a real ANSWER and it stays indistinguishable, per rule 3.
///   - <see cref="Failed"/> — the lookup could not be performed. NOT an answer. The turn fails and the
///     user sees the warning card and the retry button, because "try again" is true and "I could not
///     find it" is not.
/// </summary>
/// <param name="Data">The lookup's JSON. Empty unless the lookup completed.</param>
/// <param name="FailureReasonKey">
/// A translation key when the lookup could not be performed, null otherwise. The same keys the rest of
/// the assistant already uses, so the front end's existing resilience covers this with no change.
/// </param>
public sealed record AssistantToolOutcome(string Data, string? FailureReasonKey)
{
    /// <summary>No lookup was needed, or the model declined to ask for one. Answer without live data.</summary>
    public static readonly AssistantToolOutcome NotAttempted = new(string.Empty, null);

    /// <summary>The lookup ran and produced its answer — including a legitimate "not found".</summary>
    public static AssistantToolOutcome Completed(string data) => new(data, null);

    /// <summary>
    /// The lookup could not be performed. This must NEVER be presented to the user as a result.
    /// </summary>
    public static AssistantToolOutcome Failed(string reasonKey) => new(string.Empty, reasonKey);

    public bool DidFail => FailureReasonKey is not null;
}

/// <summary>
/// Why a lookup ended the way it did, for the OPERATOR only.
///
/// ★ THIS IS THE OTHER HALF OF THE BUG. Rule 3 makes "no such record" and "not yours" identical to the
/// USER on purpose — telling them apart would confirm the record exists. But the LOG inherited that
/// blindness for free, and it should never have: an operator reading logs could not tell a correct
/// refusal from a broken lookup, so a real fault was invisible by design.
///
/// The user-facing text does not change at all. This is what the log says.
/// </summary>
public enum AssistantToolCause
{
    /// <summary>The record was found and returned.</summary>
    Found,

    /// <summary>No record matches. A correct answer.</summary>
    NotFound,

    /// <summary>The record may exist, but this user may not read it. Also a correct answer.</summary>
    NotPermitted,

    /// <summary>The model's arguments could not be read. Nothing was looked up.</summary>
    UnreadableArguments,

    /// <summary>The lookup itself failed. A FAULT — never an answer.</summary>
    QueryFailed,
}
