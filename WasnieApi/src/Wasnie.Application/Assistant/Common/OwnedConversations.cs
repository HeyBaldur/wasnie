using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Assistant;

namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// ★ THE ONE PLACE that turns "the conversations table" into "MY conversations".
///
/// It exists for the same reason QuotaBuilder does: so a property is structural rather than a promise.
/// Every handler in this feature starts from <see cref="Mine"/>, so there is no query that could
/// accidentally omit the user half of the filter — the tenant query filter alone would happily return
/// a colleague's chat, and "remember to add UserId" is exactly the kind of rule that survives four
/// handlers and fails on the fifth.
///
/// If a shared-conversation mode is ever wanted, it gets its own entry point here. It does not get a
/// handler that filters differently.
/// </summary>
public static class OwnedConversations
{
    /// <summary>
    /// Conversations belonging to the current principal. The tenant half comes from the global query
    /// filter; the user half is applied here because a query filter cannot see the principal.
    /// </summary>
    public static IQueryable<AssistantConversation> Mine(
        IApplicationDbContext db, ICurrentUserService currentUser) =>
        db.AssistantConversations.Where(c => c.UserId == currentUser.UserId);

    /// <summary>
    /// Loads one conversation the current principal owns, or null. Null covers three cases on purpose —
    /// it does not exist, it belongs to another user, it belongs to another tenant — because telling
    /// them apart would confirm the existence of someone else's conversation.
    /// </summary>
    public static Task<AssistantConversation?> FindMineAsync(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        Guid conversationId,
        CancellationToken cancellationToken) =>
        Mine(db, currentUser)
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

    /// <summary>The message returned when a conversation is not the caller's. Same wording for all three cases.</summary>
    public const string NotFound = "Conversation not found.";

    /// <summary>
    /// The same answer as a translation key, for the streaming path — where frames carry keys rather
    /// than sentences so the client renders them in the reader's language.
    /// </summary>
    public const string NotFoundKey = "ASSISTANT.ERROR_NOT_FOUND";
}
