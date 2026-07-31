using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Assistant.DTOs;
using Wasnie.Application.Assistant.Queries;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Assistant.Handlers;

public sealed class ListConversationsHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IAssistantEntitlement entitlement)
    : IRequestHandler<ListConversationsQuery, Result<IReadOnlyList<AssistantConversationSummaryDto>>>
{
    public async Task<Result<IReadOnlyList<AssistantConversationSummaryDto>>> Handle(
        ListConversationsQuery request, CancellationToken cancellationToken)
    {
        await entitlement.RequireAsync(cancellationToken);

        // Not paginated, deliberately: this is one person's own chat history in a side panel, and the
        // server-side pagination rule exists for tenant-wide lists that grow without bound. If a real
        // user ever accumulates enough threads for this to matter, it becomes a paginated endpoint —
        // and that is a visible change, not a silent truncation, because nothing is capped here.
        var rows = await Common.OwnedConversations.Mine(db, currentUser)
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new AssistantConversationSummaryDto(
                c.Id,
                c.Title,
                c.CreatedAt,
                c.UpdatedAt,
                db.AssistantMessages.Count(m => m.ConversationId == c.Id)))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<AssistantConversationSummaryDto>>.Success(rows);
    }
}
