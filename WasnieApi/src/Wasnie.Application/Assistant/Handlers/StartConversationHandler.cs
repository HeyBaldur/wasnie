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

        // A thread with no name is a row the history list cannot render, so it always gets one. The
        // date is a placeholder for a title derived from the first message — which needs a model, so
        // it is not this piece's problem.
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? $"{DefaultTitlePrefix} {now:yyyy-MM-dd HH:mm}"
            : request.Title!;

        var conversation = AssistantConversation.Start(
            guid.NewGuid(), tenantContext.TenantId, currentUser.UserId ?? string.Empty, title, now);

        db.AssistantConversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);

        return Result<AssistantConversationDto>.Success(AssistantMapper.ToDto(conversation, []));
    }

    /// <summary>
    /// Language-neutral on purpose: the client renders its own label for an untitled thread, so the
    /// stored title does not freeze one user's language into a row every other user of the tenant
    /// might one day read in the history list.
    /// </summary>
    public const string DefaultTitlePrefix = "Chat";
}
