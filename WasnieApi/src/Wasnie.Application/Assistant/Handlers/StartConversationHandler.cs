using MediatR;
using Wasnie.Application.Assistant.Commands;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Assistant.DTOs;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Assistant;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Assistant.Handlers;

public sealed class StartConversationHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAssistantEntitlement entitlement)
    : IRequestHandler<StartConversationCommand, Result<AssistantConversationDto>>
{
    public async Task<Result<AssistantConversationDto>> Handle(
        StartConversationCommand request, CancellationToken cancellationToken)
    {
        await entitlement.RequireAsync(cancellationToken);

        var now = clock.UtcNowOffset;

        // ★ Born UNTITLED, and named by the first thing said in it — see ConversationTitle. The
        // previous "Chat 2026-07-31 14:58" told the reader nothing they could use: a history list of a
        // dozen timestamps is a list you have to open one by one.
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? AssistantConversation.UntitledSentinel
            : request.Title!;

        var conversation = AssistantConversation.Start(
            guid.NewGuid(), tenantContext.TenantId, currentUser.UserId ?? string.Empty, title, now);

        db.AssistantConversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);

        return Result<AssistantConversationDto>.Success(AssistantMapper.ToDto(conversation, []));
    }

}
