using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Assistant.Commands;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Assistant;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Assistant.Handlers;

/// <summary>
/// Pins a conversation for the caller.
///
/// ★★ THE OWNERSHIP CHECK COMES FIRST, AND IT IS NOT THE OBVIOUS ONE. The natural reading of "a pin is
/// my own preference" is that pinning somebody else's conversation harms nobody — the row would sit in
/// my list and they would never see it. It is still refused, for two reasons. Writing a row keyed to a
/// conversation id I may not read is a way of ASKING whether that id exists: pin it, and the answer to
/// "did that succeed" is "yes, something is there". And the pinned group is returned by joining back to
/// the conversations, so a pin on a thread I cannot read would either leak its title or produce a row
/// that renders as nothing. Both are avoided by the same line.
///
/// It goes through <see cref="OwnedConversations.FindMineAsync"/>, so "not mine", "another tenant's" and
/// "does not exist" are one indistinguishable answer — the rule the whole feature is built on.
/// </summary>
public sealed class PinConversationHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guids,
    IAssistantEntitlement entitlement)
    : IRequestHandler<PinConversationCommand, Result>
{
    public async Task<Result> Handle(PinConversationCommand request, CancellationToken cancellationToken)
    {
        await entitlement.RequireAsync(cancellationToken);

        var conversation = await OwnedConversations.FindMineAsync(
            db, currentUser, request.ConversationId, cancellationToken);

        if (conversation is null)
            return Result.Failure(OwnedConversations.NotFound);

        var now = clock.UtcNow;

        var state = await AssistantPins.Mine(db, currentUser)
            .FirstOrDefaultAsync(s => s.ConversationId == request.ConversationId, cancellationToken);

        // ★ ALREADY PINNED IS A SUCCESS, NOT A NO-OP TO REPORT. The button can be pressed twice, and the
        // drawer and the page can both be open on the same row; the user asked for it to be pinned and
        // it is pinned. Returning early ALSO skips the cap check below, which matters: at exactly the
        // limit, re-pinning something already pinned must not start failing.
        if (state is { PinnedAt: not null })
            return Result.Success();

        // ★★ THE CAP IS CHECKED HERE AND NOT IN A VALIDATOR, deliberately. It depends on the database
        // AND on who is asking, and FluentValidation failures throw a ValidationException whose message
        // is a sentence — this list is read in three languages and needs a KEY. A validator that
        // queries the database on behalf of the principal is a handler with a different name.
        //
        // Counted only when a pin is actually about to be added, so unpinning and re-pinning at the
        // limit behaves, and so the ordinary case does not pay for a COUNT it cannot fail.
        var pinnedCount = await AssistantPins.Mine(db, currentUser)
            .CountAsync(s => s.PinnedAt != null, cancellationToken);

        if (pinnedCount >= AssistantPins.MaxPinned)
            return Result.Failure(AssistantPins.LimitReachedKey);

        if (state is null)
        {
            state = AssistantConversationState.Create(
                guids.NewGuid(), tenantContext.TenantId, currentUser.UserId!, conversation.Id, now);

            db.AssistantConversationStates.Add(state);
        }

        state.Pin(now);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>
/// Unpins it for the caller.
///
/// ★ THE ROW IS KEPT, NOT DELETED. It is this user's standing on the conversation, and the pin is only
/// the first fact it holds — "archived", "muted" and "last read" are the same shape and land here. A
/// handler that deleted the row would have to be rewritten the moment a second fact exists, and would
/// take the second fact with it in the meantime.
/// </summary>
public sealed class UnpinConversationHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    IAssistantEntitlement entitlement)
    : IRequestHandler<UnpinConversationCommand, Result>
{
    public async Task<Result> Handle(UnpinConversationCommand request, CancellationToken cancellationToken)
    {
        await entitlement.RequireAsync(cancellationToken);

        var conversation = await OwnedConversations.FindMineAsync(
            db, currentUser, request.ConversationId, cancellationToken);

        if (conversation is null)
            return Result.Failure(OwnedConversations.NotFound);

        var state = await AssistantPins.Mine(db, currentUser)
            .FirstOrDefaultAsync(s => s.ConversationId == request.ConversationId, cancellationToken);

        // Nothing to unpin is the state the caller asked for. No row, or a row already unpinned — both
        // mean the conversation is not pinned, which is the outcome.
        if (state is null || state.PinnedAt is null)
            return Result.Success();

        state.Unpin(clock.UtcNow);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
