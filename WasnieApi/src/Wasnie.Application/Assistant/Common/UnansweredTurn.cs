using Wasnie.Domain.Assistant;

namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// Whether a conversation ends with a question nobody answered.
///
/// ★ DERIVED, NOT STORED, AND THAT IS THE DESIGN. The obvious fix for "the failed state disappears on
/// refresh" is a `HasFailed` column on the turn. It is the wrong one, for a reason that shows up later
/// and costs more than the column saved:
///
///   A stored flag is a SECOND copy of a fact the data already contains, and two copies drift. A flag
///   that says "failed" with an answer sitting right underneath it is worse than no flag at all — the
///   user is told their question failed while looking at its reply. Every path that writes an answer
///   would have to remember to clear it, forever, including paths nobody has written yet.
///
/// The absence of the reply IS the record of the failure, and it cannot lie. It also needs no
/// migration and no backfill decision for conversations that already exist.
///
/// ★ WHY THE ABSENCE IS TRUSTWORTHY. Both writers make this true and neither can stop making it true
/// without the change being obvious:
///   - The streaming path commits the user's turn BEFORE calling the model, precisely so a provider
///     failure cannot lose the question, and writes the assistant row ONLY once the stream completes.
///     Nothing partial is ever stored.
///   - The non-streaming path writes both rows in a single SaveChanges, or neither.
/// So a stored user turn with nothing after it means exactly one thing: it was asked and never
/// answered.
///
/// ★ A CANCELLED REPLY COUNTS AS AN ANSWER HERE, and that is not a loophole. The rule this asks is
/// "is the thread waiting for something?", and a turn the user themselves stopped is not waiting: the
/// partial answer is stored, it is on screen, and it carries its own mark saying where it stops.
/// Treating it as unanswered would put the failure card under it and offer a retry for a turn nobody
/// wants retried — the assistant contradicting a decision the user just made.
///
/// The one honest imprecision: a turn being answered AT THIS MOMENT also looks like this. That
/// resolves itself the instant the answer lands, and the cost of being briefly wrong is offering a
/// retry a second early — against the cost of the user losing their question, which is what this
/// replaces.
/// </summary>
public static class UnansweredTurn
{
    /// <summary>
    /// True when the last stored turn is the user's — a question that never got its reply.
    /// </summary>
    /// <remarks>
    /// Takes the messages in whatever order they arrive and orders by <c>Sequence</c> itself: ordering
    /// by timestamp would be a coin flip, because a question and its answer are written in the same
    /// instant on the non-streaming path.
    /// </remarks>
    public static bool Exists(IEnumerable<AssistantMessage> messages)
    {
        var last = messages.OrderBy(m => m.Sequence).LastOrDefault();

        return last is not null && last.Role == AssistantMessageRole.User;
    }
}
