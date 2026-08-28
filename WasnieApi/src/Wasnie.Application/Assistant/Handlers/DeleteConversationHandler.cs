using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Assistant.Commands;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Assistant.Handlers;

public sealed class DeleteConversationHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IAssistantEntitlement entitlement)
    : IRequestHandler<DeleteConversationCommand, Result>
{
    public async Task<Result> Handle(DeleteConversationCommand request, CancellationToken cancellationToken)
    {
        await entitlement.RequireAsync(cancellationToken);

        var conversation = await OwnedConversations.FindMineAsync(
            db, currentUser, request.ConversationId, cancellationToken);

        if (conversation is null)
            return Result.Failure(OwnedConversations.NotFound);

        // A real delete, not a soft one. This is someone's private chat: "deleted" has to mean gone,
        // and there is no downstream that reads a conversation after the fact. The messages follow via
        // the cascade configured on the FK — removed explicitly as well so the InMemory provider (which
        // does not enforce cascades) behaves like SQL Server in tests.
        var messages = await db.AssistantMessages
            .Where(m => m.ConversationId == conversation.Id)
            .ToListAsync(cancellationToken);

        db.AssistantMessages.RemoveRange(messages);

        // ★ AND THE STANDINGS GO WITH IT — EVERY USER'S, NOT JUST THE CALLER'S. The pin lives on a
        // (user, conversation) row, and when sharing arrives several people will have one on the same
        // thread. Deleting only mine would leave rows pointing at a conversation that no longer exists,
        // and the pinned group would silently drop them (see ListConversationsHandler) rather than
        // anybody noticing. Same pairing as the messages: the FK cascades in SQL Server, and this makes
        // the InMemory provider — which does not enforce cascades — behave the same way in tests.
        var states = await db.AssistantConversationStates
            .Where(s => s.ConversationId == conversation.Id)
            .ToListAsync(cancellationToken);

        db.AssistantConversationStates.RemoveRange(states);
        db.AssistantConversations.Remove(conversation);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
