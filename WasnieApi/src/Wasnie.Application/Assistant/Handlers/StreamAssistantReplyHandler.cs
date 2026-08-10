using System.Runtime.CompilerServices;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    AssistantToolRunner toolRunner,
    IOptions<GroqOptions> options,
    ILogger<StreamAssistantReplyHandler> logger)
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

        AssistantMessage? userMessage;

        if (request.IsRetry)
        {
            // ★ A RETRY WRITES NOTHING. The question was committed before the model was called the
            // first time — that is what made the failure survivable — so storing it again would put the
            // same message in the thread twice, and the user would watch their own words duplicate as
            // the reward for pressing Retry. The stored turn IS the question; it is re-answered, not
            // re-asked. No `user` frame either: the client already has that row on screen.
            userMessage = history.LastOrDefault(m => m.Role == AssistantMessageRole.User);

            if (userMessage is null)
            {
                // Nothing to retry — a client asking to re-answer an empty thread. Same refusal as any
                // other unusable request; there is nothing here worth a distinct message.
                yield return AssistantStreamEvent.OfError(ChatCompletionException.Unavailable);
                yield break;
            }

            // ★ THE ANSWER GOES AFTER EVERYTHING THAT IS ALREADY STORED, which is not the same thing as
            // "after the question" any more.
            //
            // A FAILED turn stored no assistant row, so the last message IS the question and this is
            // exactly the slot the first attempt would have used — unchanged behaviour.
            //
            // A CANCELLED turn did store one: the partial answer the user watched arrive and chose to
            // stop. Reusing the question's slot would write the new answer on top of a sequence that is
            // taken, and (ConversationId, Sequence) is a UNIQUE index — the retry would not produce a
            // wrong thread, it would produce a failed save. So the retry APPENDS: the stopped attempt
            // stays where it is, still marked, and the new answer lands after it. That is also the
            // honest shape of what happened — the user has both the fragment they stopped and the
            // answer they asked for again.
            nextSequence = history[^1].Sequence;
        }
        else
        {
            // C# forbids `yield return` inside a catch, so every failure below is captured into a local
            // and emitted after the try. It reads as a detour; it is the language, not the design.
            AssistantMessage? created = null;
            try
            {
                created = AssistantMessage.Create(
                    guid.NewGuid(), conversation.Id, tenantContext.TenantId,
                    AssistantMessageRole.User, request.Content, nextSequence, now);
            }
            catch (Wasnie.Domain.Exceptions.DomainException)
            {
                // Nothing written: the turn was refused before it existed.
            }

            if (created is null)
            {
                yield return AssistantStreamEvent.OfError(ChatCompletionException.Unavailable);
                yield break;
            }

            userMessage = created;

            // The user's turn is committed BEFORE the model is called. Whatever the provider does next,
            // the question is not lost — which is what makes "try again" safe rather than retyping.
            // ★ The thread takes its name from the first thing said in it. Only while untitled: a name
            // the user chose outranks a derived one, and the second message never re-titles anything.
            conversation.TitleFromFirstMessage(ConversationTitle.FromMessage(userMessage.Content), now);

            conversation.Touch(now);
            db.AssistantMessages.Add(userMessage);
            await db.SaveChangesAsync(cancellationToken);

            yield return AssistantStreamEvent.OfUser(AssistantMapper.ToDto(userMessage));

            history.Add(userMessage);
        }

        // ★ The QUESTION comes from the stored turn, never from the request body. On a retry the body
        // carries nothing meaningful, and even on a first send the stored row is the authoritative
        // version of what was asked — routing and the lookup must see the same words the model will.
        var question = userMessage.Content;

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

        // ★ THE STEPS THE USER WATCHES, AND WHY THEY ARE EMITTED FROM HERE.
        //
        // The panel used to show one fixed sentence for however long the turn took, because the browser
        // genuinely does not know what is happening — and inventing a stage on a stopwatch would be the
        // one place in this feature that asserts something it cannot verify. This handler DOES know. So
        // the steps are reported from the only place that can report them honestly, and each one is
        // emitted only on the turns where that work really occurs: a documentation question never
        // announces a database search, and a turn with no guide loaded never announces reading one.
        //
        // Every `progress` frame is ADDITIVE. It carries no answer, no stored row and no failure, so a
        // client that ignores the type behaves exactly as it did before this existed.
        var classifies = router.CanRoute || toolRunner.HasTools;

        if (classifies)
        {
            yield return AssistantStreamEvent.OfPhaseStart(AssistantPhase.Understanding);
        }

        // ── Step 1: which sections does this question need? ──────────────────
        // A small call against the table of contents only. Its result decides what step 2 carries.
        // Failing here is the same class of failure as failing to answer, and reaches the user the
        // same way — nothing was written for the assistant, so there is nothing to undo.
        IReadOnlyList<string> sectionIds = [];
        string? routingFailure = null;
        try
        {
            sectionIds = await router.RouteAsync(question, cancellationToken);
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

        // ── Step 1.5, first half: does this question need a RECORD, not just the documentation? ──
        // Read-only, through the domain, with this user's identity — see GetTransactionTool.
        //
        // ★ THE DECISION IS TAKEN BEFORE THE STEP IS ANNOUNCED, which is why the runner is now two
        // calls. "Searching your records" is only worth showing before the search and only on the turns
        // where a search happens; both need the choice to be visible before the read.
        //
        // ★ A LOOKUP THAT COULD NOT RUN ENDS THE TURN. It used to degrade to "answer without live
        // data", and the model did not treat the absence as an absence: asked about a named
        // transaction with nothing in hand, it told the user the record could not be found — about a
        // row it never queried and that they can see on their own screen. The user now gets the
        // warning card and the retry button, both of which are true, instead of a confident wrong
        // answer that looks exactly like a correct refusal.
        var selection = await toolRunner.SelectAsync(question, history, cancellationToken);

        // Both classifier calls are behind us: the one step the user was shown is genuinely finished.
        // A FAILED decision gets no `done` — the error frame below is what ends that turn.
        if (classifies && !selection.DidFail)
        {
            yield return AssistantStreamEvent.OfPhaseDone(AssistantPhase.Understanding);
        }

        if (selection.DidFail)
        {
            // Nothing was written for the assistant, so there is nothing to undo — the question stays
            // in the thread and Retry re-answers it.
            yield return AssistantStreamEvent.OfError(selection.FailureReasonKey!);
            yield break;
        }

        // Empty is a real answer, not a miss: the prompt below becomes the no-source one, which tells
        // the user plainly that the documentation does not cover this. And that is exactly why the step
        // is announced only when sections were found — "consulting the documentation" over an empty
        // routing result would describe a consultation that did not take place.
        var consultsDocs = sectionIds.Count > 0;

        if (consultsDocs)
        {
            yield return AssistantStreamEvent.OfPhaseStart(AssistantPhase.ReadingDocs);
        }

        var routed = knowledge.TextFor(sectionIds);

        if (consultsDocs)
        {
            yield return AssistantStreamEvent.OfPhaseDone(AssistantPhase.ReadingDocs);
        }

        // ── Step 1.5, second half: run what was chosen, if anything was. ──
        var toolData = string.Empty;

        if (selection.WillRead)
        {
            yield return AssistantStreamEvent.OfPhaseStart(AssistantPhase.SearchingData);

            var lookup = await toolRunner.ExecuteAsync(selection, cancellationToken);

            if (lookup.DidFail)
            {
                // No `done` for a step that failed, and no answer either: the error frame is the end of
                // this turn. The question stays in the thread and Retry re-answers it.
                yield return AssistantStreamEvent.OfError(lookup.FailureReasonKey!);
                yield break;
            }

            yield return AssistantStreamEvent.OfPhaseDone(AssistantPhase.SearchingData);

            toolData = lookup.Data;
        }

        // The navigation map rides along with step 2 and only step 2: the router chose WHAT to say from,
        // this says WHERE the user does it. Fixed context, not routed — see IUiNavigationMap.
        var prompt = AssistantPrompt.Build(
            history, options.Value.MaxHistoryMessages, routed, knowledge.IsAvailable,
            navigation.PromptBlock, toolData);
        var answer = new StringBuilder();

        // ★ THE BELT. Raising the generation model is the fix for the repetition collapse; this is what
        // guarantees the user never reads one anyway. See DegenerationGuard.
        var guard = new DegenerationGuard();

        // The last step, and the only one that is always present: something is always written, even if
        // the turn needed neither the guide nor a record.
        yield return AssistantStreamEvent.OfPhaseStart(AssistantPhase.Generating);

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
                // The client is gone: Stop was pressed, the panel was closed, the tab navigated away.
                // Nobody is listening to this stream any more — see below for what is written.
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
                // ★ THE ONE PLACE SOMETHING PARTIAL IS STORED, AND WHY IT IS NOT A HOLE IN THAT RULE.
                //
                // "Nothing partial is ever stored" exists so the user never finds a reply that stops
                // mid-sentence and reads it as the assistant's finished opinion. A cancellation is the
                // case where the partial answer is not an accident: the user WATCHED these words
                // arrive and then chose to stop them. Dropping them would erase what was on their
                // screen a moment ago and leave the thread claiming their question was never answered
                // — offering a retry for a turn they deliberately ended.
                //
                // So it is kept, and it is kept MARKED: the row carries `Cancelled`, which is what
                // stops it from ever passing for a finished answer here or in any client. That mark is
                // the whole reason storing it is safe.
                //
                // ★ NOTHING IS WRITTEN WHEN NOTHING WAS WRITTEN YET. Cancelling during the classifier
                // or the lookup leaves an empty builder, and an empty row is not a shorter answer —
                // it is a blank bubble. That turn stays unanswered, which is exactly what it is, and
                // the existing retry path covers it.
                //
                // ★ THE SAVE USES `CancellationToken.None` ON PURPOSE. The request's token is ALREADY
                // cancelled — that is how we got here — so passing it would abort the write that this
                // whole branch exists to perform, and the answer the user watched would be lost to the
                // very click meant to preserve it. The work is deliberately small and bounded: one
                // insert of text already in memory, no provider call (that connection is what just
                // ended).
                var interrupted = answer.ToString().Trim();

                if (interrupted.Length > 0)
                {
                    await PersistAssistantAsync(
                        conversation.Id, interrupted, nextSequence + 1, clock.UtcNowOffset,
                        CancellationToken.None, AssistantMessageStatus.Cancelled);
                }

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

            if (guard.Observe(fragment))
            {
                // ★ A COLLAPSE IS A TECHNICAL FAILURE, and it takes the path technical failures already
                // take: the fragment is NOT forwarded, nothing is persisted (the assistant row is only
                // written after a clean finish), and the user gets the warning card with Retry. "Try
                // again" is true; a wall of repeated words is not something anyone can act on.
                logger.LogError(
                    "The assistant's answer degenerated and was cut off: {Reason}.", guard.Reason);
                yield return AssistantStreamEvent.OfError(ChatCompletionException.Unavailable);
                yield break;
            }

            answer.Append(fragment);
            yield return AssistantStreamEvent.OfFragment(fragment);
        }

        if (guard.Finish())
        {
            // The run ended on the last word, with no trailing punctuation to close it.
            logger.LogError(
                "The assistant's answer degenerated and was cut off: {Reason}.", guard.Reason);
            yield return AssistantStreamEvent.OfError(ChatCompletionException.Unavailable);
            yield break;
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

        // Closes the last step for symmetry — every start this handler emits has exactly one matching
        // done, or an error frame in its place. The panel has long since swapped the steps for the
        // answer by now; the invariant is for whatever reads this stream next.
        yield return AssistantStreamEvent.OfPhaseDone(AssistantPhase.Generating);
        yield return AssistantStreamEvent.OfDone(AssistantMapper.ToDto(assistantMessage));
    }

    /// <param name="status">
    /// <see cref="AssistantMessageStatus.Cancelled"/> only for the interrupted branch above — every
    /// other caller is storing an answer that finished.
    /// </param>
    private async Task<AssistantMessage> PersistAssistantAsync(
        Guid conversationId, string content, int sequence, DateTimeOffset now,
        CancellationToken cancellationToken,
        AssistantMessageStatus status = AssistantMessageStatus.Complete)
    {
        // Truncated rather than rejected: a model that overruns the column has still written something
        // the user watched arrive, and refusing to store it would erase what they just read.
        var stored = content.Length > AssistantMessage.MaxContentLength
            ? content[..AssistantMessage.MaxContentLength]
            : content;

        var message = AssistantMessage.Create(
            guid.NewGuid(), conversationId, tenantContext.TenantId,
            AssistantMessageRole.Assistant, stored, sequence, now, payload: null, status: status);

        db.AssistantMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        return message;
    }
}
