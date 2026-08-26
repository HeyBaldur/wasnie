using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Assistant.DTOs;
using Wasnie.Application.Assistant.Queries;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Assistant.Handlers;

/// <summary>
/// One batch of the caller's own conversations, newest activity first.
///
/// ★ WHY THIS STOPPED BEING "ALL OF THEM". It used to return every row, on the reasoning that one
/// person's own chat history is not a tenant-wide list. That held until it did not: at a couple of
/// thousand conversations the payload is still only a few hundred kilobytes, but the DOM is two
/// thousand rows, and the drawer took a visible beat to open EVERY TIME. The cost was never the bytes.
///
/// ★ AND THE ORDER GAINED A TIEBREAKER, WHICH IS THE HALF THAT IS ABOUT CORRECTNESS. Ordering by
/// UpdatedAt alone was fine while everything arrived at once — ties could come back in any order and
/// nobody could tell. Across batches a tie is a bug: rows sharing a timestamp can repeat in the next
/// batch or vanish between two. See <see cref="ConversationCursor"/>.
/// </summary>
public sealed class ListConversationsHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IAssistantEntitlement entitlement)
    : IRequestHandler<ListConversationsQuery, Result<AssistantConversationPageDto>>
{
    public async Task<Result<AssistantConversationPageDto>> Handle(
        ListConversationsQuery request, CancellationToken cancellationToken)
    {
        await entitlement.RequireAsync(cancellationToken);

        var pageSize = request.PageSize ?? AssistantPaging.DefaultPageSize;
        var searching = AssistantPaging.IsSearchable(request.Search);

        // ★★ THE PINNED SET IS FETCHED FIRST BECAUSE THE PAGED FLOW HAS TO EXCLUDE IT. Without the
        // exclusion every pinned conversation appears TWICE — once in its own group and once in its
        // time band — and the fix has to be here rather than a dedupe in the browser: the client would
        // hide the duplicate and be left with batches of uneven size, so "25 rows" would sometimes mean
        // 23 and the end of the list would arrive early.
        //
        // ★ AND NOT WHILE SEARCHING. Searching is a different mode: the results come back flat, because
        // showing a pinned thread that does not match what was typed is noise, and hiding one that does
        // match would be inconsistent. So no pinned group, and nothing excluded from the results.
        var pinnedIds = searching
            ? []
            : await AssistantPins.PinnedIdsAsync(db, currentUser, cancellationToken);

        // ★ SCOPED FIRST, AND EVERYTHING ELSE COMPOSES ONTO IT. `Mine` is the one place that turns the
        // table into this user's rows (plus the tenant query filter underneath); the cursor and the
        // search narrow that set and can never widen it. A cursor is a position, not a permission —
        // whatever it claims, it is applied to a query that already cannot see anyone else's history.
        var query = OwnedConversations.Mine(db, currentUser);

        if (pinnedIds.Count > 0)
        {
            query = query.Where(c => !pinnedIds.Contains(c.Id));
        }

        if (searching)
        {
            // ★ CONTAINS, NOT STARTS-WITH. Titles are generated from the user's first question, so the
            // words somebody remembers are usually in the middle of one ("Cómo calculo la comisión de
            // Ana"). A prefix match would find almost nothing they actually look for.
            //
            // ★ AND CASE- AND ACCENT-INSENSITIVITY IS NOT WRITTEN HERE — it is a property of the Title
            // COLUMN, declared in AssistantConversationConfiguration. Two reasons. It keeps a
            // provider-specific collation name out of the Application layer, which has no business
            // knowing what SQL Server calls its collations. And it means every comparison against a
            // title behaves the same way, instead of this one query being insensitive while the next
            // one somebody writes is not.
            //
            // ★ NO WILDCARD ESCAPING, BECAUSE THERE ARE NO WILDCARDS. EF translates `Contains` with a
            // non-constant argument to CHARINDEX, not LIKE, so '%' and '_' in a search box are
            // ordinary characters. Reaching for EF.Functions.Like here would be the version that needs
            // escaping — and the version that silently returns the whole list when somebody types "50%".
            var term = request.Search!.Trim();
            query = query.Where(c => c.Title.Contains(term));
        }

        // ★★ THE KEYSET PREDICATE, AND THE `OR` IS THE WHOLE THING. "Strictly older, OR the same
        // instant but a lower id" is what makes (UpdatedAt, Id) behave as one totally-ordered key.
        // Written as `UpdatedAt < cursor` alone it skips every row tied with the boundary; written as
        // `<=` it returns the boundary row again on every batch, forever.
        if (ConversationCursor.Decode(request.Cursor) is { } cursor)
        {
            query = query.Where(c =>
                c.UpdatedAt < cursor.UpdatedAt
                || (c.UpdatedAt == cursor.UpdatedAt && c.Id.CompareTo(cursor.Id) < 0));
        }

        // ★ ONE ROW MORE THAN ASKED FOR, AND IT IS NEVER RETURNED. It answers "is there a next batch?"
        // without a second COUNT query over the same predicate — and, more usefully, without the
        // boundary bug of inferring "there is more" from a full batch, which promises another page that
        // turns out to be empty every time the total is an exact multiple of the page size.
        var rows = await query
            .OrderByDescending(c => c.UpdatedAt)
            .ThenByDescending(c => c.Id)
            .Take(pageSize + 1)
            .Select(c => new AssistantConversationSummaryDto(
                c.Id,
                c.Title,
                c.CreatedAt,
                c.UpdatedAt,
                db.AssistantMessages.Count(m => m.ConversationId == c.Id)))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows[..pageSize] : rows;

        // The cursor is built from the last row ACTUALLY RETURNED, never from the probe row — the next
        // batch has to start after what the user has seen, not after the one they have not.
        var nextCursor = hasMore
            ? new ConversationCursor(items[^1].UpdatedAt, items[^1].Id).Encode()
            : null;

        // ★ THE PINNED GROUP RIDES WITH THE FIRST BATCH ONLY. A continuation already has it on screen,
        // and re-sending it would make the group redraw on every "Load more".
        var pinned = pinnedIds.Count > 0 && request.Cursor is null
            ? await LoadPinnedAsync(pinnedIds, cancellationToken)
            : [];

        return Result<AssistantConversationPageDto>.Success(
            new AssistantConversationPageDto(items, nextCursor, pinned));
    }
    /// <summary>
    /// The pinned rows, in the order the ids were given — which is PinnedAt descending.
    ///
    /// ★ THE ORDER COMES FROM THE IDS, NOT FROM A SECOND SORT. `PinnedAt` lives on the state table and
    /// the titles live on the conversations; re-sorting here would mean either joining the two or
    /// re-reading a column this query does not select. The ids arrive already ordered, so the answer is
    /// to preserve that order rather than to recompute it — and the database cannot preserve it, so the
    /// reordering happens in memory, over at most AssistantPins.MaxPinned rows.
    /// </summary>
    private async Task<List<AssistantConversationSummaryDto>> LoadPinnedAsync(
        List<Guid> pinnedIds, CancellationToken cancellationToken)
    {
        var rows = await OwnedConversations.Mine(db, currentUser)
            .Where(c => pinnedIds.Contains(c.Id))
            .Select(c => new AssistantConversationSummaryDto(
                c.Id,
                c.Title,
                c.CreatedAt,
                c.UpdatedAt,
                db.AssistantMessages.Count(m => m.ConversationId == c.Id)))
            .ToListAsync(cancellationToken);

        var byId = rows.ToDictionary(r => r.Id);

        // ★ A PINNED ID WITH NO CONVERSATION IS SKIPPED, NOT ASSUMED AWAY. The cascade deletes the
        // standing when the conversation goes, so this should be impossible — and "should be impossible"
        // is not a reason to throw a KeyNotFoundException at somebody opening their chat history.
        return pinnedIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }
}
