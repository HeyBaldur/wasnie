using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Domain.Assistant;

namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// Turns a stored conversation into the message list the model receives. Shared by the streaming and
/// the non-streaming paths so the two cannot answer differently — the same reason QuotaBuilder exists.
/// </summary>
public static class AssistantPrompt
{
    /// <summary>
    /// The rules, without the documentation. Kept as its own constant so the confinement can be
    /// asserted independently of the corpus, and so the fallback below reads as one thing missing
    /// rather than two.
    ///
    /// ★ THE THREE REFUSALS ARE THE POINT. An assistant that answers everything is a worse product
    /// than one that answers less: a general-purpose reply about "clawbacks" describes an industry
    /// practice, while Wasnie's clawback has a specific design, and a user who acts on the generic
    /// answer has been misled by a tool wearing the product's badge. So: only Wasnie, only from the
    /// documentation, and say so when the documentation is silent.
    /// </summary>
    public const string ConfinementRules =
        "You are the assistant inside Wasnie, a sales-commission management product. You answer " +
        "questions about Wasnie and how it works.\n" +
        "\n" +
        "THE DOCUMENTATION BELOW IS YOUR ONLY SOURCE OF TRUTH about Wasnie. Follow these rules:\n" +
        "\n" +
        "1. ANSWER FROM THE DOCUMENTATION. When it covers the question, answer from it specifically — " +
        "name the actual behaviour, the actual rule, the actual screen. Do not give a generic " +
        "industry answer when Wasnie has a specific design; describing how commission software " +
        "usually works, when the documentation says how WASNIE works, is a wrong answer.\n" +
        "\n" +
        "2. SAY WHEN YOU DO NOT KNOW. If the documentation does not cover something, say plainly that " +
        "you do not have that information in Wasnie's documentation, and suggest they check with " +
        "their administrator. NEVER invent a feature, a setting, a screen or a workaround. Inventing " +
        "capabilities Wasnie does not have is the most damaging thing you can do: the user will try " +
        "to use them. If a rule is stricter than the user expects, state the rule as documented " +
        "rather than offering a softer alternative that does not exist.\n" +
        "\n" +
        "3. STAY ON WASNIE. If asked something unrelated to Wasnie — general employment law, sales " +
        "strategy, tax advice, or anything not about this product — do not answer it as a general " +
        "consultant. Say briefly and politely that you are Wasnie's assistant and can only help with " +
        "the product, then offer to help with something about Wasnie. Be warm about it, not curt.\n" +
        "\n" +
        "4. YOU EXPLAIN, YOU DO NOT ACT. You cannot calculate anyone's pay, create or change any " +
        "record, or run anything. When a user asks you to do something, explain how they can do it " +
        "themselves in Wasnie. Never state or imply that you have made a change.\n" +
        "\n" +
        "5. Answer in the language the user writes in, regardless of the documentation's language. " +
        "Be concise and concrete; prefer the documented specifics over general phrasing.";

    /// <summary>
    /// Used only when the documentation cannot be read. Deliberately admits it is unanchored: an
    /// assistant that keeps claiming to speak for the product while its source is missing would be
    /// confidently wrong, which is the failure mode this whole piece exists to remove.
    /// </summary>
    public const string FallbackPrompt =
        "You are the assistant inside Wasnie, a sales-commission management product. " +
        "You help users understand the product and their questions about it. " +
        "Wasnie's documentation is not available to you right now, so do not state specifics about " +
        "how Wasnie behaves unless the user has told you: say what you are unsure of and suggest they " +
        "check with their administrator. " +
        "You cannot perform actions: you do not calculate pay, create or modify any record, or run " +
        "anything in the application. Answer in the language the user writes in.";

    /// <summary>
    /// ★ THE NO-SOURCE PROMPT. Used when the router found no section that could answer — including
    /// when the question is not about Wasnie at all.
    ///
    /// This is the rule that costs the most if it fails. Without a section the assistant has NO source,
    /// and a model with no source does not stay silent — it answers from whatever it absorbed in
    /// training, fluently and with the product's badge on it. In a system that decides what people are
    /// paid, that is the worst output the feature can produce. So the absence of a source is turned
    /// into an explicit instruction to say so, rather than left as an empty context to fill.
    /// </summary>
    public const string NoSourcePrompt =
        "You are the assistant inside Wasnie, a sales-commission management product.\n" +
        "\n" +
        "Wasnie's documentation contains NOTHING that answers this question. You therefore have no " +
        "source for it, and you must not answer it from general knowledge.\n" +
        "\n" +
        "Reply briefly and warmly that you do not have information about this in Wasnie's " +
        "documentation. If the question is about Wasnie, suggest they ask their administrator. If it " +
        "is not about Wasnie at all, say that you are Wasnie's assistant and can only help with the " +
        "product, then offer to help with something about it.\n" +
        "\n" +
        "Do NOT invent a feature, a setting, a screen or a workaround. Do NOT explain the topic in " +
        "general terms. Do NOT claim to have performed any action. Answer in the language the user " +
        "writes in, in two sentences or fewer.";

    /// <summary>Wraps the corpus so the model can tell documentation from instruction.</summary>
    public const string DocumentationHeader = "=== WASNIE DOCUMENTATION (your only source of truth) ===";

    public const string DocumentationFooter = "=== END OF WASNIE DOCUMENTATION ===";

    /// <summary>
    /// The system message: the rules, then the documentation, then a short restatement.
    ///
    /// The rules are repeated after the corpus on purpose. Fifteen thousand tokens separate an
    /// instruction placed before the document from the user's question, and instructions nearest the
    /// end of the context carry the most weight — the reminder is cheap and it is what keeps the
    /// refusals from being buried by the material they apply to.
    /// </summary>
    public static string BuildSystemMessage(string documentation) =>
        BuildSystemMessage(documentation, documentationAvailable: true);

    /// <param name="documentationAvailable">
    /// False only when the corpus could not be READ at all. It separates two different silences: a
    /// guide that is missing (the assistant is unanchored and says so) from a guide that simply does
    /// not cover the question (the assistant says THAT, which is a real and useful answer).
    /// </param>
    public static string BuildSystemMessage(string documentation, bool documentationAvailable)
    {
        if (string.IsNullOrWhiteSpace(documentation))
        {
            return documentationAvailable ? NoSourcePrompt : FallbackPrompt;
        }

        return $"""
            {ConfinementRules}

            {DocumentationHeader}
            {documentation}
            {DocumentationFooter}

            Remember: answer only from the documentation above, say so when it does not cover the
            question, decline questions that are not about Wasnie, and never claim to have performed
            an action.
            """;
    }

    /// <summary>
    /// The system message, then the last <paramref name="maxHistory"/> turns in order.
    ///
    /// The cap keeps the NEWEST turns: a long thread would otherwise grow every request without bound,
    /// and the oldest turns are the ones contributing least to the answer.
    ///
    /// Stand-in replies from the unconfigured days are dropped — replaying "the assistant is not
    /// connected yet" as if the assistant had said it would teach the model to say it again.
    /// </summary>
    /// <param name="routedDocumentation">
    /// ONLY the sections the router chose — never the whole guide. Sending everything exceeded the
    /// provider's per-request token allowance and no question got through at all.
    /// </param>
    public static IReadOnlyList<ChatMessage> Build(
        IReadOnlyList<AssistantMessage> history,
        int maxHistory,
        string routedDocumentation,
        bool documentationAvailable)
    {
        var usable = history
            .Where(m => m.Content != AssistantMessage.NotConnectedPlaceholder)
            .OrderBy(m => m.Sequence)
            .ToList();

        if (maxHistory > 0 && usable.Count > maxHistory)
        {
            usable = usable.Skip(usable.Count - maxHistory).ToList();
        }

        var messages = new List<ChatMessage>(usable.Count + 1)
        {
            new(ChatMessage.SystemRole, BuildSystemMessage(routedDocumentation, documentationAvailable)),
        };

        messages.AddRange(usable.Select(m => new ChatMessage(
            m.Role == AssistantMessageRole.Assistant ? ChatMessage.AssistantRole : ChatMessage.UserRole,
            m.Content)));

        return messages;
    }
}
