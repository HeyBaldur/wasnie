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
    ILogger<StreamAssistantReplyHandler> logger,
    IModelUsageRecorder usageRecorder)
    : IStreamRequestHandler<StreamAssistantReplyCommand, AssistantStreamEvent>
{
    /// <summary>
    /// Runs the turn and logs where its time went (KAN-74).
    ///
    /// ★ A WRAPPER, SO THE LOG LINE CANNOT BE SKIPPED. The turn below has a dozen exits — errors, a stopped stream, a
    /// refused allowance — and a timing log placed at the end of the happy path would only ever measure the turns that
    /// went well. The `finally` runs on every one of them, including when the client disconnects and the enumerator is
    /// disposed mid-stream.
    /// </summary>
    public async IAsyncEnumerable<AssistantStreamEvent> Handle(
        StreamAssistantReplyCommand request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var timer = new AssistantTurnTimer();
        var outcome = "abandoned";

        try
        {
            await foreach (var e in HandleTurn(request, timer, cancellationToken).WithCancellation(cancellationToken))
            {
                if (e.Type == AssistantStreamEvent.Done && e.Message is not null)
                {
                    outcome = "done";
                }
                else if (e.Type == AssistantStreamEvent.Error)
                {
                    outcome = e.ErrorKey ?? "error";
                }

                yield return e;
            }
        }
        finally
        {
            var t = timer.Snapshot();
            logger.LogInformation(
                "Assistant turn timing ({Outcome}): total {TotalMs} ms = provider {ProviderMs} ms + ours {OursMs} ms. " +
                "Provider: classifiers {ClassifiersMs} ms (router {RouterMs} ms, dispatcher {DispatcherMs} ms, in parallel), answer first token {AnswerTtftMs} ms of {AnswerMs} ms. " +
                "Ours: before the first model call {BeforeFirstModelCallMs} ms, tool {ToolMs} ms.",
                outcome, t.TotalMs, t.ProviderMs, t.OursMs,
                t.ClassifiersMs, t.RouterMs, t.DispatcherMs, t.AnswerTimeToFirstTokenMs, t.AnswerMs,
                t.BeforeFirstModelCallMs, t.ToolMs);
        }
    }

    private async IAsyncEnumerable<AssistantStreamEvent> HandleTurn(
        StreamAssistantReplyCommand request,
        AssistantTurnTimer timer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await entitlement.RequireAsync(cancellationToken);

        // KAN-77: a trial account that used up its assistant allowance gets no NEW turn — nothing is stored,
        // the model is not called. A retry re-answers a question already counted, so it is not refused here.
        if (!request.IsRetry && await entitlement.IsTrialAllowanceExhaustedAsync(cancellationToken))
        {
            yield return AssistantStreamEvent.OfError(IAssistantEntitlement.TrialAllowanceExhaustedKey);
            yield break;
        }

        var conversation = await OwnedConversations.FindMineAsync(
            db, currentUser, request.ConversationId, cancellationToken);

        if (conversation is null)
        {
            yield return AssistantStreamEvent.OfError(OwnedConversations.NotFoundKey);
            yield break;
        }

        // KAN-80: every model call below is charged to this account and attributed to this conversation.
        usageRecorder.ForConversation(conversation.Id);

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

            // ★ THE TITLE GOES OUT WITH THE TURN THAT DECIDED IT. It was set two lines above and
            // committed in the same SaveChanges, so this frame is the first honest moment to say what
            // the conversation is now called — and the only one before the model answers.
            yield return AssistantStreamEvent.OfUser(
                AssistantMapper.ToDto(userMessage), conversation.Title);

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

        // ── Steps 1 and 1.5 (first half), IN PARALLEL: which sections, and does it need a record? ──
        //
        // ★★ PARALLEL SINCE KAN-74, AND THAT IS SAFE BECAUSE NEITHER DEPENDS ON THE OTHER. The router reads only the
        // question against the table of contents; the dispatcher reads the question and the thread already in memory.
        // Neither touches the DbContext (which is not thread-safe) and neither's input is the other's output. In series
        // the turn waited for both one after the other — measured at 1 to 9 s of pure waiting per turn, on greetings too.
        //
        // The router: a small call against the table of contents only. Its result decides what step 2 carries.
        // Failing here is the same class of failure as failing to answer, and reaches the user the same way — nothing
        // was written for the assistant, so there is nothing to undo.
        //
        // The dispatcher: does this question need a RECORD, not just the documentation? Read-only, through the domain,
        // with this user's identity — see GetTransactionTool.
        //
        // ★ THE DECISION IS TAKEN BEFORE THE STEP IS ANNOUNCED, which is why the runner is two calls. "Searching your
        // records" is only worth showing before the search and only on the turns where a search happens; both need the
        // choice to be visible before the read.
        //
        // ★ A LOOKUP THAT COULD NOT RUN ENDS THE TURN. It used to degrade to "answer without live data", and the model
        // did not treat the absence as an absence: asked about a named transaction with nothing in hand, it told the
        // user the record could not be found — about a row it never queried and that they can see on their own screen.
        // The user now gets the warning card and the retry button, both of which are true, instead of a confident wrong
        // answer that looks exactly like a correct refusal.
        //
        // ★ A FAILED ROUTER CANCELS THE DISPATCHER. The turn is over either way, so its call is not left running to
        // spend tokens on an answer nobody will read — and it is still AWAITED, so nothing faults unobserved.
        using var classifiers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        async Task<(IReadOnlyList<string> Ids, string? Failure, bool Cancelled)> RouteAsync()
        {
            timer.RouterStarted();
            try
            {
                return (await router.RouteAsync(question, classifiers.Token), null, false);
            }
            catch (ChatCompletionException ex)
            {
                return ([], ex.ReasonKey, false);
            }
            catch (OperationCanceledException)
            {
                return ([], null, true);
            }
            finally
            {
                timer.RouterEnded();
            }
        }

        async Task<AssistantToolSelection?> SelectAsync()
        {
            timer.DispatcherStarted();
            try
            {
                return await toolRunner.SelectAsync(question, history, classifiers.Token);
            }
            catch (OperationCanceledException)
            {
                // Null = cancelled, which is not a selection of any kind.
                return null;
            }
            finally
            {
                timer.DispatcherEnded();
            }
        }

        var routingTask = RouteAsync();
        var selectionTask = SelectAsync();

        var routing = await routingTask;

        if (routing.Failure is not null || routing.Cancelled)
        {
            await classifiers.CancelAsync();
            await selectionTask;

            if (routing.Cancelled)
            {
                yield break;
            }

            yield return AssistantStreamEvent.OfError(routing.Failure!);
            yield break;
        }

        IReadOnlyList<string> sectionIds = routing.Ids;

        var selected = await selectionTask;

        if (selected is null)
        {
            // The client went away while the dispatcher was deciding.
            yield break;
        }

        var selection = selected;

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

            timer.ToolStarted();
            var lookup = await toolRunner.ExecuteAsync(selection, cancellationToken);
            timer.ToolEnded();

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

        // ★ EXTRACTED HERE, WHERE THE PAYLOAD STILL EXISTS. Everything below can fail, be stopped, or
        // degenerate; the ids are a fact about the lookup that already succeeded and do not depend on
        // any of it. See ResolvedEntityContext — this is the one line that keeps them alive past the
        // end of the request.
        var resolvedPayload = ResolvedEntityContext.PayloadFor(toolData);

        // ★★ MERGED, NEVER ASSIGNED ONE OVER THE OTHER (KAN-58). Both of these serialise a COMPLETE
        // payload object with a single key in it, so storing whichever was computed last would silently
        // drop the other — and losing the resolved entities breaks nothing visibly: it just makes the
        // next turn ask for a name this thread had already resolved. See AssistantClarify.Merge.
        var turnPayload = AssistantClarify.Merge(
            resolvedPayload, AssistantClarify.PayloadFor(toolData));

        // The navigation map rides along with step 2 and only step 2: the router chose WHAT to say from,
        // this says WHERE the user does it. Fixed context, not routed — see IUiNavigationMap.
        var prompt = AssistantPrompt.Build(
            history, options.Value.MaxHistoryMessages, routed, knowledge.IsAvailable,
            navigation.PromptBlock, toolData);
        var answer = new StringBuilder();

        // ★ THE BELT. Raising the generation model is the fix for the repetition collapse; this is what
        // guarantees the user never reads one anyway. See DegenerationGuard.
        var guard = new DegenerationGuard();

        // ★★ THE SECOND BELT, AND ITS GROUNDING IS THE PROMPT ITSELF (KAN-58). `prompt` is literally
        // everything the model is given — the conversation history including the question just asked,
        // the routed documentation, the navigation map and the tool's JSON — because it is what the
        // provider is called with, on the line below. So "did this identifier come from somewhere?" is
        // answerable by asking whether it occurs in here, and no separate bookkeeping can drift out of
        // step with what was actually sent.
        //
        // ★ THE CONTENTS ARE CONCATENATED AND THE ROLES ARE DROPPED. The question is only ever "does
        // this token occur?", and which turn it occurred in does not change the answer.
        var fabrication = new FabricationGuard(
            string.Join('\n', prompt.Select(m => m.Content)));

        // The last step, and the only one that is always present: something is always written, even if
        // the turn needed neither the guide nor a record.
        yield return AssistantStreamEvent.OfPhaseStart(AssistantPhase.Generating);

        // The enumerator is stepped by hand so a provider failure can be caught: `yield return` is not
        // allowed inside a try/catch that has a catch clause, and wrapping the whole loop would mean
        // choosing between catching errors and streaming at all.
        timer.AnswerStarted();
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
                    timer.AnswerFragment();
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
                    // ★ THE STOPPED TURN KEEPS ITS IDS. The user stopped the PROSE; the lookup behind it
                    // ran to completion and really did resolve a payee. Dropping the context here would
                    // mean the next question after a Stop quietly falls back to retyping the name — the
                    // exact failure this channel exists to end, reappearing on the one path where the
                    // user has already shown they are in a hurry.
                    await PersistAssistantAsync(
                        conversation.Id, interrupted, nextSequence + 1, clock.UtcNowOffset,
                        CancellationToken.None, AssistantMessageStatus.Cancelled, resolvedPayload);
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
                timer.AnswerEnded();
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

            // ★★ CHECKED BEFORE THE FRAGMENT IS FORWARDED, exactly like the degeneration check above.
            // An identifier that reached the user's screen has already done its damage: they cannot
            // tell it from the real figures beside it, which is the whole finding of KAN-58.
            if (fabrication.Observe(fragment))
            {
                logger.LogError(
                    "The assistant invented an identifier and the answer was abandoned: {Token}.",
                    fabrication.Fabricated);
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

        // ★ THE COMPLETE SCAN. Observe only re-reads a tail, so a fabrication early in a long answer
        // slides out of its window; this is the pass that cannot miss one. It runs before the row is
        // persisted, which is what keeps an invented identifier out of the stored history as well as
        // off the screen.
        if (fabrication.Finish())
        {
            logger.LogError(
                "The assistant invented an identifier and the answer was abandoned: {Token}.",
                fabrication.Fabricated);
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
            conversation.Id, text, nextSequence + 1, clock.UtcNowOffset, cancellationToken,
            resolvedPayload: turnPayload);

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
    /// <param name="resolvedPayload">
    /// The identifiers this turn's lookup resolved, serialised — null on every turn that looked nothing
    /// up. See <see cref="ResolvedEntityContext"/>: this column is the ONLY thing that carries an id to
    /// the next turn, because the answer text is forbidden from mentioning one and the tool payload is
    /// discarded when the request ends.
    /// </param>
    private async Task<AssistantMessage> PersistAssistantAsync(
        Guid conversationId, string content, int sequence, DateTimeOffset now,
        CancellationToken cancellationToken,
        AssistantMessageStatus status = AssistantMessageStatus.Complete,
        string? resolvedPayload = null)
    {
        // ★ NOT TRUNCATED IN SILENCE ANY MORE. Replies used to be cut to the USER's 8,000-character limit here, so a
        // long answer the user had watched arrive in full was stored — and re-rendered — cut mid-sentence (§B1). The
        // reply limit now equals the degeneration guard's ceiling, so a reply that got this far fits. If that ever
        // stops being true, the cut is still made (refusing would erase what the user read) but it is LOGGED.
        var stored = content;
        if (content.Length > AssistantMessage.MaxReplyLength)
        {
            logger.LogError(
                "An assistant reply of {Length} characters exceeded the stored limit of {Limit} and was truncated.",
                content.Length, AssistantMessage.MaxReplyLength);
            stored = content[..AssistantMessage.MaxReplyLength];
        }

        var message = AssistantMessage.Create(
            guid.NewGuid(), conversationId, tenantContext.TenantId,
            AssistantMessageRole.Assistant, stored, sequence, now,
            payload: resolvedPayload, status: status);

        db.AssistantMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        return message;
    }
}
