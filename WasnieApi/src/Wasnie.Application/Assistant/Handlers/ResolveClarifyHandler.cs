using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Assistant.Commands;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Assistant;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Assistant.Handlers;

/// <summary>
/// Records what the person did with a clarify form: they picked an option, or they closed it (KAN-58).
///
/// ★★ IT IS THE ONLY THING IN THE ASSISTANT THAT MODIFIES A STORED TURN, and the exception is argued on
/// <see cref="AssistantMessage.ReplacePayload"/>: the form is an interactive control the user acts on
/// AFTER the row exists, unlike the words the assistant said and unlike how the turn ended, both of
/// which are history and stay immutable. Nothing here can reach Content or Status.
///
/// ★★ THE STATE IS WHY THE FORM DOES NOT COME BACK. The ticket's third comment asks for exactly this:
/// a refresh restores the conversation with the form as the user left it — answered stays answered,
/// closed stays closed — rather than a fresh panel reappearing over a question they already dealt with.
/// It is stored on the row precisely so that it survives without the client having to remember.
///
/// ★ OWNERSHIP FIRST, VIA THE SHARED HELPER. "Not mine", "another tenant's" and "does not exist" come
/// back as one indistinguishable answer, like everywhere else in this feature: a caller must not be
/// able to discover that a conversation id exists by trying to close a form on it.
/// </summary>
public sealed class ResolveClarifyHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IAssistantEntitlement entitlement)
    : IRequestHandler<ResolveClarifyCommand, Result>
{
    public async Task<Result> Handle(ResolveClarifyCommand request, CancellationToken cancellationToken)
    {
        await entitlement.RequireAsync(cancellationToken);

        if (!ClarifyState.IsKnown(request.State) || request.State == ClarifyState.Open)
        {
            // ★ open IS REFUSED ALONG WITH THE UNKNOWN VALUES. Moving a form BACK to open would let a
            // client resurrect a panel the user had closed, which is the one behaviour the ticket's
            // third comment rules out by name.
            return Result.Failure(OwnedConversations.NotFound);
        }

        var conversation = await OwnedConversations.FindMineAsync(
            db, currentUser, request.ConversationId, cancellationToken);

        if (conversation is null)
            return Result.Failure(OwnedConversations.NotFound);

        var message = await db.AssistantMessages
            .FirstOrDefaultAsync(
                m => m.Id == request.MessageId && m.ConversationId == conversation.Id,
                cancellationToken);

        if (message is null)
            return Result.Failure(OwnedConversations.NotFound);

        var form = AssistantClarify.Extract(message.Payload);

        // ★ A TURN WITH NO FORM IS NOT AN ERROR TO SHOUT ABOUT, but it is not a success either: the
        // caller asked to close something that is not there, and telling them it worked would hide a
        // client that has drifted from the stored conversation.
        if (form is null)
            return Result.Failure(OwnedConversations.NotFound);

        // ★★ ALREADY RESOLVED IS A SUCCESS. Two tabs open on the same thread, or a double click, must
        // not produce an error — the user asked for the form to be closed and it is closed. It also
        // must not OVERWRITE: a form answered in one tab stays answered even if the other tab then
        // sends a dismiss, because the answer is the more informative of the two facts.
        if (form.State != ClarifyState.Open)
            return Result.Success();

        message.ReplacePayload(AssistantClarify.WithState(message.Payload, request.State));

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
