using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Assistant;

namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// The rules the pinned group lives under, and the one place that turns the state table into
/// "MY standings" — the same job <see cref="OwnedConversations"/> does for the conversations.
/// </summary>
public static class AssistantPins
{
    /// <summary>
    /// How many conversations one user may keep pinned.
    ///
    /// ★★ THERE HAS TO BE A CEILING, AND IT IS NOT A TIDINESS RULE. Pinned conversations are returned
    /// OUTSIDE the cursor — completely, with the first batch — because a pin that fell off the first
    /// page would defeat the entire feature. That is only affordable while the set is bounded: without
    /// a cap, somebody who pins everything reintroduces exactly the unbounded first response the paging
    /// work item existed to remove, and does it on a path with no "load more" to fall back on.
    ///
    /// Fifty is a starting number, not a computed one: far past what anybody pins in practice, far
    /// short of a payload anyone would notice.
    /// </summary>
    public const int MaxPinned = 50;

    /// <summary>
    /// What the user is told when they hit the ceiling.
    ///
    /// ★ A TRANSLATION KEY, NOT A SENTENCE. The history list is read in English, Spanish and Polish;
    /// a message composed here would arrive in one of them and stay that way. Same contract the
    /// streaming frames use.
    /// </summary>
    public const string LimitReachedKey = "ASSISTANT.PIN_LIMIT_REACHED";

    /// <summary>
    /// This user's standings. The tenant half comes from the global query filter; the user half is
    /// applied here, because a query filter cannot see the principal.
    /// </summary>
    public static IQueryable<AssistantConversationState> Mine(
        IApplicationDbContext db, ICurrentUserService currentUser) =>
        db.AssistantConversationStates.Where(s => s.UserId == currentUser.UserId);

    /// <summary>The ids this user currently has pinned, newest pin first.</summary>
    public static Task<List<Guid>> PinnedIdsAsync(
        IApplicationDbContext db, ICurrentUserService currentUser, CancellationToken cancellationToken) =>
        Mine(db, currentUser)
            .Where(s => s.PinnedAt != null)
            .OrderByDescending(s => s.PinnedAt)
            .Select(s => s.ConversationId)
            .ToListAsync(cancellationToken);
}
