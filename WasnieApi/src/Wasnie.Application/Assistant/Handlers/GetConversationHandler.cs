using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Assistant.DTOs;
using Wasnie.Application.Assistant.Queries;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Assistant.Handlers;

public sealed class GetConversationHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IAssistantEntitlement entitlement)
    : IRequestHandler<GetConversationQuery, Result<AssistantConversationDto>>
{
    public async Task<Result<AssistantConversationDto>> Handle(
        GetConversationQuery request, CancellationToken cancellationToken)
    {
        await entitlement.RequireAsync(cancellationToken);

        // ★ Ownership is checked by LOADING THE CONVERSATION THROUGH THE OWNED QUERY, not by reading a
        // row and comparing fields afterwards. A conversation that is not mine is indistinguishable
        // from one that does not exist — including in the error message, because a different message
        // would confirm that someone else's conversation is there.
        var conversation = await OwnedConversations.FindMineAsync(
            db, currentUser, request.ConversationId, cancellationToken);

        if (conversation is null)
            return Result<AssistantConversationDto>.Failure(OwnedConversations.NotFound);

        // Sequence, not CreatedAt: a user turn and its reply are written in the same instant, so a
        // timestamp sort would be a coin flip between the question and the answer.
        var messages = await db.AssistantMessages
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.Sequence)
            .ToListAsync(cancellationToken);

        return Result<AssistantConversationDto>.Success(
            AssistantMapper.ToDto(conversation, messages));
    }
}
