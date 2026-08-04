using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Assistant.Commands;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Assistant.DTOs;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Assistant.Handlers;

public sealed class RenameConversationHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    IAssistantEntitlement entitlement)
    : IRequestHandler<RenameConversationCommand, Result<AssistantConversationSummaryDto>>
{
    public async Task<Result<AssistantConversationSummaryDto>> Handle(
        RenameConversationCommand request, CancellationToken cancellationToken)
    {
        await entitlement.RequireAsync(cancellationToken);

        var conversation = await OwnedConversations.FindMineAsync(
            db, currentUser, request.ConversationId, cancellationToken);

        if (conversation is null)
            return Result<AssistantConversationSummaryDto>.Failure(OwnedConversations.NotFound);

        try
        {
            conversation.Rename(request.Title, clock.UtcNowOffset);
        }
        catch (DomainException ex)
        {
            return Result<AssistantConversationSummaryDto>.Failure(ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);

        var messageCount = await db.AssistantMessages
            .CountAsync(m => m.ConversationId == conversation.Id, cancellationToken);

        return Result<AssistantConversationSummaryDto>.Success(
            AssistantMapper.ToSummary(conversation, messageCount));
    }
}
