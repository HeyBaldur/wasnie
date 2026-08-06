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
        if (_tools.Count == 0)
        {
            return AssistantToolOutcome.NotAttempted;
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
            return AssistantToolOutcome.NotAttempted;
        }
        catch (ChatCompletionException ex)
        {
            // ★ NOT "answer without live data" ANY MORE. The provider being unreachable, timed out or
            // rate limited is a FAULT, and letting the turn continue meant the model answered a
            // question about a specific record having looked at nothing — and then said it could not
            // find it. The user now gets the warning card and the retry button, which are both true.
            logger.LogWarning(ex, "Tool selection could not be performed ({Reason}); failing the turn.", ex.ReasonKey);
            return AssistantToolOutcome.Failed(ex.ReasonKey);
        }

        if (request is null)
        {
            // The ordinary case: a documentation question needs no lookup.
            return AssistantToolOutcome.NotAttempted;
        }

        var tool = _tools.FirstOrDefault(t =>
            string.Equals(t.Schema.Name, request.Name, StringComparison.OrdinalIgnoreCase));

        if (tool is null)
        {
            // A model naming a tool that does not exist is a hallucination like any other, and it is
            // dropped like any other. It must NOT become a lookup of something else that seemed close.
            logger.LogWarning("The assistant asked for an unknown tool {Tool}; ignored.", request.Name);
            return AssistantToolOutcome.NotAttempted;
        }

        try
        {
            var result = await tool.RunAsync(request.ArgumentsJson, cancellationToken);

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

        var messages = new List<ChatMessage>(usable.Count + 2)
        {
            new(ChatMessage.SystemRole, SelectionInstructions),
        };

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
        "You are a lookup dispatcher inside Wasnie, a sales-commission product. You do NOT answer " +
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
        "Never guess an identifier that appears nowhere — not in the last message and not in the " +
        "context. Never call a tool just in case.";
}
