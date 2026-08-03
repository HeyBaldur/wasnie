using System.Runtime.CompilerServices;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Assistant.Commands;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Assistant;

namespace Wasnie.Application.Assistant.Handlers;

/// <summary>
/// The connected exchange: persist the user's turn, stream the model's answer, persist it when it ends.
///
/// ★ ISOLATION IS UNCHANGED. The conversation is loaded through <see cref="OwnedConversations"/>, the
/// same gate every other assistant handler starts from, so a user cannot make the model read — or write
/// into — someone else's thread. Connecting a model did not widen anything.
///
/// ★ NOTHING PARTIAL IS EVER STORED. Fragments go to the client as they arrive, but the assistant row
/// is written only once the stream completes. If the provider dies halfway the user sees an error and
/// the conversation holds their question and no answer — which is the truth. The alternative, storing
/// whatever arrived, leaves a reply that stops mid-sentence and looks like the assistant's opinion.
///
/// ★ NO RETRIEVAL HERE. The prompt is the minimal one in <see cref="AssistantPrompt"/>; confining the
/// assistant to Wasnie's documentation is the next piece, deliberately not this one.
/// </summary>
public sealed class StreamAssistantReplyHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAssistantEntitlement entitlement,
    IChatCompletionProvider provider,
    IAssistantKnowledgeBase knowledge,
    IUiNavigationMap navigation,
    AssistantSectionRouter router,
    IOptions<GroqOptions> options)
    : IStreamRequestHandler<StreamAssistantReplyCommand, AssistantStreamEvent>
{
    public async IAsyncEnumerable<AssistantStreamEvent> Handle(
        StreamAssistantReplyCommand request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await entitlement.RequireAsync(cancellationToken);

        var conversation = await OwnedConversations.FindMineAsync(
            db, currentUser, request.ConversationId, cancellationToken);

        if (conversation is null)
        {
            yield return AssistantStreamEvent.OfError(OwnedConversations.NotFoundKey);
            yield break;
        }

        var now = clock.UtcNowOffset;

        var history = await db.AssistantMessages
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.Sequence)
            .ToListAsync(cancellationToken);

        var nextSequence = history.Count == 0 ? 0 : history[^1].Sequence + 1;

        // C# forbids `yield return` inside a catch, so every failure below is captured into a local and
        // emitted after the try. It reads as a detour; it is the language, not the design.
        AssistantMessage? userMessage = null;
        try
        {
            userMessage = AssistantMessage.Create(
                guid.NewGuid(), conversation.Id, tenantContext.TenantId,
                AssistantMessageRole.User, request.Content, nextSequence, now);
        }
        catch (Wasnie.Domain.Exceptions.DomainException)
        {
            // Nothing written: the turn was refused before it existed.
        }

        if (userMessage is null)
        {
            yield return AssistantStreamEvent.OfError(ChatCompletionException.Unavailable);
            yield break;
        }

        // The user's turn is committed BEFORE the model is called. Whatever the provider does next,
        // the question is not lost — which is what makes "try again" safe rather than retyping.
        // ★ The thread takes its name from the first thing said in it. Only while untitled: a name the
        // user chose outranks a derived one, and the second message never re-titles anything.
        conversation.TitleFromFirstMessage(ConversationTitle.FromMessage(userMessage.Content), now);

        conversation.Touch(now);
        db.AssistantMessages.Add(userMessage);
        await db.SaveChangesAsync(cancellationToken);

        yield return AssistantStreamEvent.OfUser(AssistantMapper.ToDto(userMessage));

        history.Add(userMessage);

        // Not configured → the stand-in reply, exactly as before a model existed. A developer without
        // a key still gets a working panel instead of an error they cannot fix.
        if (!provider.IsConfigured)
        {
            var placeholder = await PersistAssistantAsync(
                conversation.Id, AssistantMessage.NotConnectedPlaceholder, nextSequence + 1, now, cancellationToken);

            yield return AssistantStreamEvent.OfFragment(AssistantMessage.NotConnectedPlaceholder);
            yield return AssistantStreamEvent.OfDone(AssistantMapper.ToDto(placeholder));
            yield break;
        }

        // ── Step 1: which sections does this question need? ──────────────────
        // A small call against the table of contents only. Its result decides what step 2 carries.
        // Failing here is the same class of failure as failing to answer, and reaches the user the
        // same way — nothing was written for the assistant, so there is nothing to undo.
        IReadOnlyList<string> sectionIds = [];
        string? routingFailure = null;
        try
        {
            sectionIds = await router.RouteAsync(request.Content, cancellationToken);
        }
        catch (ChatCompletionException ex)
        {
            routingFailure = ex.ReasonKey;
        }
        catch (OperationCanceledException)
        {
            yield break;
        }

        if (routingFailure is not null)
        {
            yield return AssistantStreamEvent.OfError(routingFailure);
            yield break;
        }

        // Empty is a real answer, not a miss: the prompt below becomes the no-source one, which tells
        // the user plainly that the documentation does not cover this.
        var routed = knowledge.TextFor(sectionIds);

        // The navigation map rides along with step 2 and only step 2: the router chose WHAT to say from,
        // this says WHERE the user does it. Fixed context, not routed — see IUiNavigationMap.
        var prompt = AssistantPrompt.Build(
            history, options.Value.MaxHistoryMessages, routed, knowledge.IsAvailable, navigation.PromptBlock);
        var answer = new StringBuilder();

        // The enumerator is stepped by hand so a provider failure can be caught: `yield return` is not
        // allowed inside a try/catch that has a catch clause, and wrapping the whole loop would mean
        // choosing between catching errors and streaming at all.
        await using var fragments = provider.StreamAsync(prompt, cancellationToken).GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            string? fragment = null;
            string? failureKey = null;
            var abandoned = false;

            try
            {
                if (await fragments.MoveNextAsync())
                {
                    fragment = fragments.Current;
                }
            }
            catch (ChatCompletionException ex)
            {
                failureKey = ex.ReasonKey;
            }
            catch (OperationCanceledException)
            {
                // The user closed the panel or navigated away. There is nobody left to tell.
                abandoned = true;
            }
            catch (Exception)
            {
                // Anything unforeseen still reaches the user as a plain "unavailable" rather than as a
                // stack trace or a vendor sentence.
                failureKey = ChatCompletionException.Unavailable;
            }

            if (abandoned)
            {
                yield break;
            }

            if (failureKey is not null)
            {
                // Nothing was written for the assistant, so there is nothing to roll back — the user
                // keeps their question and gets an error instead of half an answer.
                yield return AssistantStreamEvent.OfError(failureKey);
                yield break;
            }

            if (fragment is null)
            {
                break;
            }

            answer.Append(fragment);
            yield return AssistantStreamEvent.OfFragment(fragment);
        }

        var text = answer.ToString().Trim();

        if (text.Length == 0)
        {
            // A stream that ended without a word is a failure wearing a success's clothes: persisting
            // an empty assistant row would render as a blank bubble the user cannot interpret.
            yield return AssistantStreamEvent.OfError(ChatCompletionException.Unavailable);
            yield break;
        }

        var assistantMessage = await PersistAssistantAsync(
            conversation.Id, text, nextSequence + 1, clock.UtcNowOffset, cancellationToken);

        yield return AssistantStreamEvent.OfDone(AssistantMapper.ToDto(assistantMessage));
    }

    private async Task<AssistantMessage> PersistAssistantAsync(
        Guid conversationId, string content, int sequence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Truncated rather than rejected: a model that overruns the column has still written something
        // the user watched arrive, and refusing to store it would erase what they just read.
        var stored = content.Length > AssistantMessage.MaxContentLength
            ? content[..AssistantMessage.MaxContentLength]
            : content;

        var message = AssistantMessage.Create(
            guid.NewGuid(), conversationId, tenantContext.TenantId,
            AssistantMessageRole.Assistant, stored, sequence, now);

        db.AssistantMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        return message;
    }
}
