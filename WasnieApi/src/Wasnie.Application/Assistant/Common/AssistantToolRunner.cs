using Microsoft.Extensions.Logging;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Domain.Assistant;

namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// Step 1.5 of the answer: decide whether this question needs live data, and if so, fetch it.
///
/// The full shape is: the router picks documentation sections (step 1), THIS asks the model whether a
/// lookup is needed and runs it, and the generating call (step 2) receives both. One round of tool use
/// per turn — enough for a single read-only lookup, and the ceiling is deliberate: a loop that lets the
/// model call tools until it is satisfied is a different feature with a different cost profile, and it
/// is not what "read one transaction" needs.
///
/// ★ SHARED BY BOTH ANSWER PATHS for the same reason AssistantPrompt is: the streaming and
/// non-streaming replies must not be able to differ in what they know.
///
/// ★ A BROKEN LOOKUP IS NOT AN ANSWER — a position this file has reversed, and the reversal is the
/// point. It used to swallow every failure and let the turn continue "without live data", on the
/// reasoning that an optional enrichment failing should not cost the answer. That reasoning was wrong
/// for one specific reason: the model does not treat missing data as missing. Asked about a named
/// transaction with nothing in hand, it told the user the record could not be found — a claim about a
/// row it never queried, that the user can see on their own screen. A wrong answer delivered
/// confidently is worse than an error, so infrastructure failures now fail the turn.
/// </summary>
public sealed class AssistantToolRunner(
    IChatCompletionProvider provider,
    IEnumerable<IAssistantTool> tools,
    ILogger<AssistantToolRunner> logger)
{
    private readonly IReadOnlyList<IAssistantTool> _tools = tools.ToList();

    /// <summary>
    /// Whether a dispatcher call will happen at all.
    ///
    /// Asked by the streaming handler before it announces the "understanding" step: with no tools
    /// registered there is no decision to make, and a step that reports work nobody did is exactly the
    /// dishonesty the progress events exist to avoid.
    /// </summary>
    public bool HasTools => _tools.Count > 0;

    /// <summary>
    /// What the lookup did — see <see cref="AssistantToolOutcome"/> for why this is no longer a string.
    ///
    /// ★ THE DISTINCTION THAT WAS MISSING. "Nothing to look up" and "the lookup broke" used to be the
    /// same empty string, so a fault reached the model as silence — and the model filled the silence by
    /// telling the user their record could not be found. A generation CHOICE (no tool wanted, or a call
    /// the provider rejected) is still not fatal. An INFRASTRUCTURE failure now is.
    /// </summary>
    /// <param name="history">
    /// The conversation so far, so the dispatcher can resolve a FOLLOW-UP.
    ///
    /// ★ THE BUG THIS ARGUMENT EXISTS TO FIX, and it was structural rather than stochastic. The
    /// dispatcher used to receive the current message and nothing else. Turn 1 — "explain the plan Q3
    /// 2026 — Plan Comercial EMEA (Test Integral)" — carried the name and worked. Turn 2 — "I have a
    /// transaction for 149.000 for 200 laptops, how many credits does it generate?" — names no plan,
    /// because a person does not repeat the title they just used. The dispatcher, seeing only that
    /// sentence, sent `{"planName": null}`, and the user was told their plan could not be found two
    /// messages after having it explained to them.
    ///
    /// No amount of name normalisation reaches that: there was no name to normalise. What was missing
    /// was the conversation.
    /// </param>
    public async Task<AssistantToolOutcome> RunAsync(
        string question,
        IReadOnlyList<AssistantMessage> history,
        CancellationToken cancellationToken)
    {
        var selection = await SelectAsync(question, history, cancellationToken);

        return selection.DidFail
            ? AssistantToolOutcome.Failed(selection.FailureReasonKey!)
            : await ExecuteAsync(selection, cancellationToken);
    }

    /// <summary>
    /// The DECISION half: which lookup, if any, this question needs. Runs the dispatcher; touches no data.
    ///
    /// ★ WHY THIS IS SEPARATE FROM RUNNING IT. The streaming handler has to tell the user "searching
    /// your records" BEFORE the search happens — announcing it afterwards is a progress indicator that
    /// only ever reports the past. It also must NOT announce it on the turns where the dispatcher
    /// declines, which are most of them. Only a caller that can see the decision before the read can do
    /// both, so the two halves are two calls. <see cref="RunAsync"/> is still the whole thing for the
    /// non-streaming path, which has nobody to report to.
    /// </summary>
    public async Task<AssistantToolSelection> SelectAsync(
        string question,
        IReadOnlyList<AssistantMessage> history,
        CancellationToken cancellationToken)
    {
        if (_tools.Count == 0)
        {
            return AssistantToolSelection.None;
        }

        AssistantToolRequest? request;
        try
        {
            request = await provider.SelectToolAsync(
                BuildSelectionMessages(question, history),
                _tools.Select(t => t.Schema).ToList(),
                cancellationToken);
        }
        catch (ChatCompletionException ex) when (ex.ReasonKey == ChatCompletionException.ToolCallRejected)
        {
            // ★ THE 400 THAT LOOKED LIKE A BUG. The provider rejected the model's own generated call
            // (`tool_use_failed`). Observed to be stochastic, not deterministic — the identical request
            // succeeds on the next attempt — so it is a generation fumble, not a broken contract, and
            // the right response is to carry on without the lookup rather than to fail the turn.
            // Logged at INFORMATION because it is expected occasionally; a warning for something that
            // happens by design trains people to ignore warnings.
            logger.LogInformation(
                "The model produced a tool call the provider rejected; answering without live data.");
            return AssistantToolSelection.None;
        }
        catch (ChatCompletionException ex)
        {
            // ★ NOT "answer without live data" ANY MORE. The provider being unreachable, timed out or
            // rate limited is a FAULT, and letting the turn continue meant the model answered a
            // question about a specific record having looked at nothing — and then said it could not
            // find it. The user now gets the warning card and the retry button, which are both true.
            logger.LogWarning(ex, "Tool selection could not be performed ({Reason}); failing the turn.", ex.ReasonKey);
            return AssistantToolSelection.Failed(ex.ReasonKey);
        }

        if (request is null)
        {
            // The ordinary case: a documentation question needs no lookup.
            return AssistantToolSelection.None;
        }

        var tool = _tools.FirstOrDefault(t =>
            string.Equals(t.Schema.Name, request.Name, StringComparison.OrdinalIgnoreCase));

        if (tool is null)
        {
            // A model naming a tool that does not exist is a hallucination like any other, and it is
            // dropped like any other. It must NOT become a lookup of something else that seemed close.
            logger.LogWarning("The assistant asked for an unknown tool {Tool}; ignored.", request.Name);
            return AssistantToolSelection.None;
        }

        return AssistantToolSelection.Of(tool, request.ArgumentsJson);
    }

    /// <summary>
    /// The READING half: runs the lookup the dispatcher chose. A selection that chose nothing is not an
    /// error — it returns <see cref="AssistantToolOutcome.NotAttempted"/>, which is the ordinary case.
    /// </summary>
    public async Task<AssistantToolOutcome> ExecuteAsync(
        AssistantToolSelection selection, CancellationToken cancellationToken)
    {
        if (selection.DidFail)
        {
            return AssistantToolOutcome.Failed(selection.FailureReasonKey!);
        }

        if (selection.Tool is null)
        {
            return AssistantToolOutcome.NotAttempted;
        }

        var tool = selection.Tool;

        try
        {
            var result = await tool.RunAsync(selection.ArgumentsJson ?? string.Empty, cancellationToken);

            // ★ The ARGUMENTS are not logged, for the same reason the question is not: they are the
            // user's own words about their own records, and a log is not where those belong.
            logger.LogInformation("The assistant ran the read-only tool {Tool}.", tool.Schema.Name);

            return AssistantToolOutcome.Completed(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // ★ A BROKEN LOOKUP IS NOT AN ANSWER. This used to return empty, and the model then told
            // the user their transaction could not be found — about a row it had never queried and
            // that they could see on screen. A fault fails the turn: "try again" is true, "I could not
            // find it" was not.
            logger.LogError(ex, "The read-only tool {Tool} failed; failing the turn.", tool.Schema.Name);
            return AssistantToolOutcome.Failed(ChatCompletionException.Unavailable);
        }
    }

    /// <summary>
    /// How many earlier messages the dispatcher is shown.
    ///
    /// Four is two exchanges: enough for "that plan" to have a referent, short enough that the
    /// dispatcher is deciding about the CURRENT question rather than re-litigating an old one. This
    /// call is a classifier, and a classifier handed a whole thread starts answering the wrong turn.
    /// </summary>
    private const int MaxContextMessages = 4;

    /// <summary>
    /// How much of each earlier message is shown.
    ///
    /// The assistant's own replies run to three thousand characters of tables; the dispatcher needs the
    /// NAMES in them, not the tables. Truncating keeps this call cheap and fast — it is on the critical
    /// path of every turn — while preserving what a reference points at.
    /// </summary>
    private const int MaxContextCharacters = 600;

    /// <summary>
    /// The instructions, the recent turns, then the question — the question LAST, so the dispatcher
    /// decides about it and treats what came before as context.
    ///
    /// The current turn is dropped from the history if the caller already appended it: the two answer
    /// paths differ on that, and a dispatcher reading the same sentence twice is being told it matters
    /// twice.
    /// </summary>
    private static IReadOnlyList<ChatMessage> BuildSelectionMessages(
        string question, IReadOnlyList<AssistantMessage> history)
    {
        var usable = history
            .Where(m => m.Content != AssistantMessage.NotConnectedPlaceholder)
            .OrderBy(m => m.Sequence)
            .ToList();

        // The caller may or may not have appended the current question already.
        if (usable.Count > 0 && usable[^1].Content == question)
        {
            usable.RemoveAt(usable.Count - 1);
        }

        if (usable.Count > MaxContextMessages)
        {
            usable = usable.Skip(usable.Count - MaxContextMessages).ToList();
        }

        var messages = new List<ChatMessage>(usable.Count + 3)
        {
            new(ChatMessage.SystemRole, SelectionInstructions),
        };

        // ★★ THE RESOLVED-ENTITY CONTEXT, AND IT IS BUILT FROM THE **WHOLE** THREAD.
        //
        // Note what it is NOT derived from: `usable`, the four truncated messages below. Those are
        // capped so the classifier decides about the current question instead of re-litigating an old
        // one, and truncated because it needs the names in a reply and not its tables. Neither limit
        // should reach the ids — an id is two dozen characters, it cannot be "mostly" right, and a payee
        // resolved six turns ago is exactly the one a comparison reaches back for. So the context is
        // taken from `history` in full while the transcript stays short.
        //
        // Placed BEFORE the turns on purpose: it is reference data the dispatcher looks things up in,
        // not a thing that was said. Put after them it would be the most recent "message" in the thread
        // and the classifier would start deciding about it.
        var resolved = ResolvedEntityContext.PromptBlock(ResolvedEntityContext.From(history));

        if (resolved.Length > 0)
        {
            messages.Add(new ChatMessage(ChatMessage.SystemRole, resolved));
        }

        messages.AddRange(usable.Select(m => new ChatMessage(
            m.Role == AssistantMessageRole.Assistant ? ChatMessage.AssistantRole : ChatMessage.UserRole,
            m.Content.Length > MaxContextCharacters ? m.Content[..MaxContextCharacters] : m.Content)));

        messages.Add(new ChatMessage(ChatMessage.UserRole, question));

        return messages;
    }

    /// <summary>
    /// ★ COLD, LIKE THE ROUTER'S. This call decides one thing and must not start composing an answer:
    /// personality here invites a reply instead of a decision, and a friendly preamble is what breaks
    /// the parse. It is also told to abstain, because calling a lookup for a question the handbook
    /// answers spends a database round trip to answer nothing.
    ///
    /// ★ THE ABSTENTION RULE HAD TO BE NARROWED, and this is the integration the second tool needed.
    /// It used to read "if the message is about how the product works, call NO tool" — which is exactly
    /// how a reasonable dispatcher classifies "how does my plan pay?", and that question is now
    /// answerable from the tenant's real configuration. Left alone, the plan tool would have been
    /// registered and never called. The distinction is no longer record-vs-explanation but THIS
    /// TENANT'S DATA vs THE PRODUCT'S BEHAVIOUR: what MY plan does is data; what a plan IS is
    /// documentation.
    /// </summary>
    public const string SelectionInstructions =
        "You are a lookup dispatcher inside Incentra, a sales-commission product. You do NOT answer " +
        "questions and you do NOT write prose.\n" +
        "\n" +
        "Decide whether answering the user's message requires reading THIS TENANT'S OWN DATA — a " +
        "specific record, or how their own plans are actually configured. If it does, call the matching " +
        "tool.\n" +
        "\n" +
        "Call a tool when the message asks about a specific transaction, deal or sale by reference; or " +
        "about how a plan pays, what rules or rates a plan has, how a plan is configured, or why a " +
        "commission came out the way it did. Questions about the user's OWN plan count even when no " +
        "plan is named — call the plan tool without a name and it will ask which one.\n" +
        "\n" +
        "★ A QUESTION ABOUT A PERSON IS NOT A QUESTION ABOUT A PLAN, and the two have DIFFERENT tools. " +
        "\"What plans is Ana on\", \"which plan is she assigned to\", \"does he have a plan\", \"since " +
        "when is she on it\" are about a PAYEE's assignments: call the payee-plans tool with the payee. " +
        "\"How does the EMEA plan pay\", \"what are its rules\", \"what is the rate\" are about a PLAN's " +
        "configuration: call the plan-rules tool with the plan. NEVER pass a PERSON'S NAME as a plan " +
        "name — no plan is called \"Ana García\", the lookup will refuse, and the user will be told the " +
        "person does not exist one turn after you described them.\n" +
        "\n" +
        "★ AND THE MIRROR OF IT, WHICH IS THE ONE THAT ACTUALLY BIT. There is NO tool that lists " +
        "the payees on a plan. The assignment tool runs PAYEE to PLANS and only that way. So when the " +
        "user asks who is on a plan, how many payees a plan has, or whether a plan has anybody " +
        "assigned, do NOT reach for the payee tools and do NOT feed them the plan’s name or the " +
        "plan’s id: no person is called “Q3 EMEA Accelerator” and no person has a plan’s " +
        "UUID, so the lookup will truthfully answer that nobody like that exists — about a plan the " +
        "user is reading on their screen. Call NO tool for that question. The answering model has a " +
        "rule for saying honestly that the capability does not exist yet; it cannot use that rule once " +
        "you have handed it a real not-found.\n" +
        "\n" +
        "Call NO tool when the message is about what a term means, how the product works in general, or " +
        "how to perform an action in the interface. The documentation answers those and a lookup would " +
        "return nothing useful.\n" +
        "\n" +
        "Earlier messages in the conversation are given to you as CONTEXT. Decide about the LAST user " +
        "message, but resolve what it refers to from that context: if the user asked about a plan by " +
        "name and now says \"this plan\", \"that plan\", or asks a follow-up that plainly concerns it " +
        "without naming it, pass that plan's name — copied EXACTLY as it appeared earlier, in full. A " +
        "follow-up question is not a new subject.\n" +
        "\n" +
        IdentifierRules +
        "\n" +
        "Never guess an identifier that appears nowhere — not in the last message, not in the context " +
        "and not in the resolved-entity block. Never call a tool just in case.";

    /// <summary>
    /// ★★ THE IRON RULE — AND IT IS HERE, IN THE DISPATCHER'S PROMPT, BECAUSE THAT IS THE ONLY PLACE IT
    /// CAN DO ANYTHING.
    ///
    /// It used to live in <c>AssistantPrompt.DataRules</c> as rules 10b/10c, addressed to the model that
    /// composes the ANSWER — a model that never calls a tool and has no arguments to fill in. It was a
    /// correct instruction delivered to the wrong reader, and it had no effect on any lookup. Worse, it
    /// LOOKED like the fix, so the real gap (no channel, see ResolvedEntityContext) stayed invisible
    /// while the continuity bug kept reproducing.
    ///
    /// ★ TRANSVERSAL BETWEEN TOOLS, WHICH IS THE WHOLE POINT. A payee's id is the same id for their
    /// balance, their assignments and anything added later, and the rule is written about the ENTITY
    /// rather than about the tool that resolved it. The reproduced failure was precisely a jump between
    /// tools: turn 1 resolved Ana through the balance lookup, turn 2 asked about her plans, and the
    /// dispatcher — with no rule spanning the two — went back to typing her name.
    ///
    /// ★ AND IT PERMITS MORE THAN ONE. "Never look up a name twice" written carelessly reads as "use THE
    /// id", which quietly forbids comparisons: two payees resolved in two turns are two entries, and
    /// answering about one of them is not the question that was asked.
    /// </summary>
    public const string IdentifierRules =
        "RESOLVED ENTITIES. You may be given a block listing payees and plans that have ALREADY been " +
        "looked up in this conversation, each with its id and its canonical name, most recent first.\n" +
        "\n" +
        "- IF THE MESSAGE REFERS TO ONE OF THEM, PASS ITS id AS THE TOOL ARGUMENT (payeeId, planId) AND " +
        "DO NOT PASS THE NAME. This includes implicit references — \"and his plans?\", \"what about her " +
        "balance?\", \"that plan\", \"the same person\" — which almost always mean the FIRST entry of " +
        "the matching kind, because it is the one the previous turn was about.\n" +
        "- IT IS FORBIDDEN TO SEARCH BY NAME FOR AN ENTITY THAT IS ALREADY IN THAT BLOCK. Retyping a " +
        "name from memory is how a person the assistant just described comes back as \"not found\".\n" +
        "- THE ID CROSSES TOOLS. A payee has ONE id whatever question is asked about them: if the " +
        "balance lookup resolved them, pass that same id to the assignments lookup, and the other way " +
        "round. It does not matter which tool put the entity in the block.\n" +
        "- SEVERAL ENTITIES CAN BE THERE AT ONCE, and you are not limited to the first. \"Compare her " +
        "with Bruno\" means matching each name to its own entry and using each id. Match by name to " +
        "decide WHICH entry a sentence means.\n" +
        "- SEARCH BY NAME ONLY for something that is NOT in the block: a new payee or plan named for the " +
        "first time in this conversation.\n" +
        "\n";
}
