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
/// ★ A FAILING TOOL IS NOT A FAILING ANSWER. If the selection call or the lookup itself breaks, this
/// returns no data and the assistant answers from the documentation alone. The alternative — turning a
/// question into an error because an optional enrichment failed — is worse for the user and hides the
/// fault from nobody, since it is logged either way.
/// </summary>
public sealed class AssistantToolRunner(
    IChatCompletionProvider provider,
    IEnumerable<IAssistantTool> tools,
    ILogger<AssistantToolRunner> logger)
{
    private readonly IReadOnlyList<IAssistantTool> _tools = tools.ToList();

    /// <summary>
    /// The lookup's JSON, or empty when no tool was called (or one failed).
    /// </summary>
    public async Task<string> RunAsync(string question, CancellationToken cancellationToken)
    {
        if (_tools.Count == 0)
        {
            return string.Empty;
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
            return string.Empty;
        }
        catch (ChatCompletionException ex)
        {
            logger.LogWarning(ex, "Tool selection failed; the assistant will answer without live data.");
            return string.Empty;
        }

        if (request is null)
        {
            return string.Empty;
        }

        var tool = _tools.FirstOrDefault(t =>
            string.Equals(t.Schema.Name, request.Name, StringComparison.OrdinalIgnoreCase));

        if (tool is null)
        {
            // A model naming a tool that does not exist is a hallucination like any other, and it is
            // dropped like any other. It must NOT become a lookup of something else that seemed close.
            logger.LogWarning("The assistant asked for an unknown tool {Tool}; ignored.", request.Name);
            return string.Empty;
        }

        try
        {
            var result = await tool.RunAsync(request.ArgumentsJson, cancellationToken);

            // ★ The ARGUMENTS are not logged, for the same reason the question is not: they are the
            // user's own words about their own records, and a log is not where those belong.
            logger.LogInformation("The assistant ran the read-only tool {Tool}.", tool.Schema.Name);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The read-only tool {Tool} failed; answering without live data.", tool.Schema.Name);
            return string.Empty;
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
