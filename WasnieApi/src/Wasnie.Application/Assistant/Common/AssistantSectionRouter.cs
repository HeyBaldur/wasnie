using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Assistant.Abstractions;

namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// Step one of two: asks the model which sections of the guide a question needs.
///
/// ★ WHY A MODEL AND NOT KEYWORDS. Matching words is brittle in the way that matters here — a user who
/// asks about "someone leaving with commissions already paid" is asking about clawback without using
/// the word, and no amount of stemming finds that. The model reads twenty-one titles and answers
/// semantically. The call is small: the table of contents is a few hundred tokens, which is the entire
/// input cost of deciding what the second call carries.
///
/// ★ AND IT IS LEGIBLE. The routing decision comes back as plain JSON and is logged. When an answer is
/// wrong, the log says whether the router picked the wrong section or the generation fumbled the right
/// one — a distinction a vector similarity score cannot give you.
/// </summary>
public sealed class AssistantSectionRouter(
    IChatCompletionProvider provider,
    IAssistantKnowledgeBase knowledge,
    ILogger<AssistantSectionRouter> logger)
{
    /// <summary>
    /// Cold and mechanical on purpose. This model is not talking to anyone: personality here would
    /// invite it to answer the question instead of routing it, and a chatty preamble is exactly what
    /// breaks JSON parsing.
    /// </summary>
    public const string RouterInstructions =
        "You are a lookup router. You do NOT answer questions and you do NOT explain anything.\n" +
        "\n" +
        "Below is the table of contents of a product manual, as `id: title` lines. Decide which " +
        "sections would contain the answer to the user's question. Judge by MEANING, not by matching " +
        "words: a question about someone leaving the company with commissions already paid belongs to " +
        "a section about clawback even if it never uses that word.\n" +
        "\n" +
        "BE GENEROUS. If the question is about this product at all, choose sections — 1 to 4 of them, " +
        "most relevant first. Broad questions deserve broad answers: \"what can this product do\", " +
        "\"what is a plan\", \"list what it covers\" are all answerable, and should route to the " +
        "overview and the sections that cover the topic. When you are unsure WHICH section, choose the " +
        "two or three most likely ones. Choosing too many is a small waste; choosing none is a wrong " +
        "answer to a user who asked something reasonable.\n" +
        "\n" +
        "Return an empty list ONLY when the question is genuinely not about this product — cooking, " +
        "the weather, general employment law, small talk. \"I could not decide\" is NOT a reason to " +
        "return an empty list.\n" +
        "\n" +
        "Use the ids EXACTLY as they appear on the left of each line, including the letter prefix.\n" +
        "\n" +
        "Return ONLY a JSON object of the form {\"sections\": [\"s3\", \"s17\"]}. No prose, no explanation.";

    /// <summary>
    /// The ids the question needs. An EMPTY list is a real answer — "nothing here covers this" — and
    /// the caller must treat it as such rather than as a failure.
    /// </summary>
    public async Task<IReadOnlyList<string>> RouteAsync(string question, CancellationToken cancellationToken)
    {
        if (!knowledge.IsAvailable || knowledge.Sections.Count == 0)
        {
            return [];
        }

        var messages = new List<ChatMessage>
        {
            new(ChatMessage.SystemRole, $"{RouterInstructions}\n\nTABLE OF CONTENTS:\n{knowledge.TableOfContents}"),
            new(ChatMessage.UserRole, question),
        };

        string json;
        try
        {
            json = await provider.CompleteJsonAsync(messages, cancellationToken);
        }
        catch (ChatCompletionException)
        {
            // Let the caller decide. A router that cannot route is not automatically a failed answer:
            // the generation step has a no-source prompt that says so honestly.
            throw;
        }

        var ids = Parse(json);

        // ★ THE OBSERVABILITY WIN. Titles, not just ids, so the log is readable a month from now.
        //
        // The QUESTION is deliberately NOT logged. These conversations are private to one user by
        // design — that is the whole premise of the feature — and copying them into operator logs
        // would quietly undo it. The decision is what needs auditing; if a specific question routes
        // badly, it can be reproduced by asking it.
        var titles = ids
            .Select(id => knowledge.Sections.FirstOrDefault(s => s.Id == id)?.Title ?? $"unknown:{id}")
            .ToList();

        if (ids.Count == 0)
        {
            logger.LogInformation("Assistant router selected NO sections; the answer will say so.");
        }
        else
        {
            logger.LogInformation(
                "Assistant router selected sections {SectionIds} ({SectionTitles}).",
                string.Join(",", ids), string.Join(" | ", titles));
        }

        return ids;
    }

    /// <summary>
    /// Reads the id list out of the router's JSON.
    ///
    /// Returns EMPTY on anything unparseable rather than throwing. Structured-output mode makes a
    /// non-JSON reply very unlikely, and an empty list degrades into "I do not have that in the
    /// documentation" — which is a safe thing to say and a true one when we could not find a section.
    /// Throwing here would turn a soft miss into a broken chat.
    /// </summary>
    public static IReadOnlyList<string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var result = JsonSerializer.Deserialize<RouterResult>(json);
            return result?.Sections?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record RouterResult(
        [property: JsonPropertyName("sections")] IReadOnlyList<string>? Sections);
}
