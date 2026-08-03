using Microsoft.Extensions.Logging;
using Wasnie.Application.Assistant.Abstractions;

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
    public async Task<AssistantToolOutcome> RunAsync(string question, CancellationToken cancellationToken)
    {
        if (_tools.Count == 0)
        {
            return AssistantToolOutcome.NotAttempted;
        }

        AssistantToolRequest? request;
        try
        {
            request = await provider.SelectToolAsync(
                [new ChatMessage(ChatMessage.SystemRole, SelectionInstructions),
                 new ChatMessage(ChatMessage.UserRole, question)],
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
    /// ★ COLD, LIKE THE ROUTER'S. This call decides one thing and must not start composing an answer:
    /// personality here invites a reply instead of a decision, and a friendly preamble is what breaks
    /// the parse. It is also told to abstain, because calling a lookup for a question about how the
    /// product works spends a database round trip to answer nothing.
    /// </summary>
    public const string SelectionInstructions =
        "You are a lookup dispatcher inside Wasnie, a sales-commission product. You do NOT answer " +
        "questions and you do NOT write prose.\n" +
        "\n" +
        "Decide whether answering the user's message requires looking up a SPECIFIC RECORD in Wasnie. " +
        "If it does, call the matching tool with the identifier the user gave. If the message is about " +
        "how the product works, what a term means, or how to do something, call NO tool — the " +
        "documentation answers those and a lookup would return nothing useful.\n" +
        "\n" +
        "Never guess an identifier the user did not write. Never call a tool just in case.";
}
