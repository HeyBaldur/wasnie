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

    /// <summary>Wraps the navigation map, so routes read as data and not as an example to imitate.</summary>
    public const string NavigationHeader = "=== WASNIE NAVIGATION MAP (the only routes that exist) ===";

    public const string NavigationFooter = "=== END OF WASNIE NAVIGATION MAP ===";

    /// <summary>
    /// The rules that turn an explanation into a set of steps somebody can follow.
    ///
    /// ★ RULE 6 IS THE ONE THAT MATTERS, and it is deliberately written as strictly as rule 2. A model
    /// asked to guide will produce a URL that LOOKS right — `/admin/create-plan` reads perfectly and is
    /// a 404 — for exactly the reason it invents features: a plausible shape is easy to generate and the
    /// model cannot tell it apart from a real one. Rule 2 already stops invented FEATURES and holds; this
    /// is the same discipline pointed at routes, and it must be as absolute, because a link is worse than
    /// a wrong sentence. A user reading a wrong sentence may doubt it. A user CLICKS a link, and the
    /// dead end arrives at the moment they finally acted.
    ///
    /// The fallback in rule 6 is what makes the strictness usable rather than paralysing: no route in
    /// the map is not "say nothing", it is "name the screen without a link".
    /// </summary>
    public const string NavigationRules =
        "6. NEVER INVENT A URL. When you tell the user to do something in Wasnie, give them the link — " +
        "but ONLY a route that appears verbatim in the navigation map below. If the action has no route " +
        "in the map, name the screen and say how to reach it, WITHOUT a link. A route that is not in " +
        "the map does not exist, however sensible it looks. Do not guess, do not adapt a route by " +
        "substituting an id or a name into it, and do not build one by analogy with another.\n" +
        "\n" +
        "7. GIVE STEPS, NOT ADVICE. When the user asks how to do something, answer with a NUMBERED LIST " +
        "of the actual steps, in order. Put the exact name of every button, field and screen in bold — " +
        "for example: click **New Plan**, fill in **Name**, choose the **Currency**. Use the names " +
        "exactly as the navigation map spells them; the user is reading the same words on screen.\n" +
        "\n" +
        "8. LINK FORMAT: relative Markdown, starting with a slash — [Go to new plan](/plans/new). Never " +
        "write a full address with a domain: the link must stay inside the application the user is " +
        "already in.";

    /// <summary>Wraps a tool's answer, so live data reads as data and not as more documentation.</summary>
    public const string DataHeader = "=== LIVE DATA (looked up just now, for this user, from Wasnie) ===";

    public const string DataFooter = "=== END OF LIVE DATA ===";

    /// <summary>
    /// How the model must treat what a tool returned.
    ///
    /// ★ RULE 9 IS THE ONE THAT PROTECTS THE REFUSAL. When the lookup comes back with `found: false`,
    /// the model must relay that and stop. Left to itself it would soften the answer into something
    /// helpful and wrong — "it may still be processing", "check with your administrator, it was
    /// probably voided" — and each of those is a claim about a record it was just told it cannot see.
    /// The refusal is deliberately identical for "does not exist" and "not yours"; a model that
    /// speculates past it undoes that in one sentence.
    ///
    /// ★ RULE 11 KEEPS "EXPLAIN, DO NOT ACT" TRUE NOW THAT DATA IS REACHABLE. Reading is not doing.
    /// The tool cannot write, and the assistant must not imply that asking it to would change anything.
    /// </summary>
    public const string DataRules =
        "9. THE LIVE DATA BELOW IS THE ONLY THING YOU KNOW ABOUT THIS RECORD. Report it as it is. If it " +
        "says found is false, tell the user plainly that you could not find that transaction or do not " +
        "have access to it, and STOP — do not speculate about why, do not suggest what might have " +
        "happened to it, and do not offer reasons it might be missing. Anything you add there is a " +
        "claim about a record you cannot see.\n" +
        "\n" +
        "10. USE THE FIELD NAMES' MEANING. saleAmount is what the customer paid; commissionAmount is " +
        "what the payee earned — never present one as the other. hasBeenPaid true means the money has " +
        "gone out; a payoutStatus of Calculated or Approved is NOT paid. If a field says something is " +
        "not visible to you, say that part is not available to this user rather than inventing it or " +
        "reporting it as zero.\n" +
        "\n" +
        "11. YOU LOOKED IT UP, YOU DID NOT CHANGE IT. This lookup is read-only. You cannot create, " +
        "edit, void, recalculate or pay anything, and you must never imply otherwise. If the user asks " +
        "you to change what you just read, explain where THEY can do it.";

    /// <summary>
    /// The rules restated after the corpus, so what the model reads last is what it must obey.
    /// </summary>
    private const string Reminder =
        "Remember: answer only from the documentation above, say so when it does not cover the " +
        "question, decline questions that are not about Wasnie, and never claim to have performed " +
        "an action.";

    private const string NavigationReminder =
        "When you tell the user to do something, give numbered steps with the exact button names in " +
        "bold, and link ONLY to routes listed in the navigation map. Never invent a URL.";

    /// <summary>
    /// The system message: the rules, then the documentation, then a short restatement.
    ///
    /// The rules are repeated after the corpus on purpose. Fifteen thousand tokens separate an
    /// instruction placed before the document from the user's question, and instructions nearest the
    /// end of the context carry the most weight — the reminder is cheap and it is what keeps the
    /// refusals from being buried by the material they apply to.
    /// </summary>
    public static string BuildSystemMessage(string documentation) =>
        BuildSystemMessage(documentation, documentationAvailable: true, navigationMap: string.Empty);

    public static string BuildSystemMessage(string documentation, bool documentationAvailable) =>
        BuildSystemMessage(documentation, documentationAvailable, navigationMap: string.Empty);

    /// <param name="documentationAvailable">
    /// False only when the corpus could not be READ at all. It separates two different silences: a
    /// guide that is missing (the assistant is unanchored and says so) from a guide that simply does
    /// not cover the question (the assistant says THAT, which is a real and useful answer).
    /// </param>
    /// <param name="navigationMap">
    /// The app's real routes. Empty when the map could not be read — the assistant then guides without
    /// links, which rule 6 makes the correct degradation rather than an invitation to guess.
    ///
    /// ★ NOT SENT WITH THE NO-SOURCE PROMPT, on purpose. That prompt is used when the documentation
    /// answers nothing, and its whole job is to say so. Handing it a list of screens at that moment is
    /// an invitation to fill the silence with navigation — the user would be walked confidently into a
    /// screen for a capability nobody confirmed exists. No source means no guidance, links included.
    /// </param>
    public static string BuildSystemMessage(string documentation, bool documentationAvailable, string navigationMap) =>
        BuildSystemMessage(documentation, documentationAvailable, navigationMap, toolData: string.Empty);

    /// <param name="toolData">
    /// What a read-only tool just looked up for THIS user, or empty when no tool ran.
    ///
    /// ★ LIVE DATA IS A SOURCE, so its presence overrides the no-source prompt. "What happened with
    /// TERM-CC-10?" is a question the handbook cannot answer and the record can — routing it to "the
    /// documentation does not cover this" while the answer sits in the context would be the feature
    /// failing with its own result in hand.
    /// </param>
    public static string BuildSystemMessage(
        string documentation, bool documentationAvailable, string navigationMap, string toolData)
    {
        var hasData = !string.IsNullOrWhiteSpace(toolData);
        var dataBlock = hasData
            ? $"\n{DataRules}\n\n{DataHeader}\n{toolData}\n{DataFooter}\n"
            : string.Empty;

        if (string.IsNullOrWhiteSpace(documentation))
        {
            if (!hasData)
            {
                return documentationAvailable ? NoSourcePrompt : FallbackPrompt;
            }

            // Documentation silent, record in hand: answer FROM the record and nothing else. The
            // confinement rules still apply — they are what stops "and here is how clawbacks generally
            // work" being appended to a factual answer about one sale.
            return $"""
                {ConfinementRules}
                {dataBlock}
                Remember: the live data above is what you know. Report it as it is, say plainly when it
                could not be found, never invent anything around it, and never claim to have changed it.
                """;
        }

        if (string.IsNullOrWhiteSpace(navigationMap))
        {
            return $"""
                {ConfinementRules}

                {DocumentationHeader}
                {documentation}
                {DocumentationFooter}
                {dataBlock}
                {Reminder}
                """;
        }

        return $"""
            {ConfinementRules}

            {NavigationRules}

            {DocumentationHeader}
            {documentation}
            {DocumentationFooter}

            {NavigationHeader}
            {navigationMap}
            {NavigationFooter}
            {dataBlock}
            {Reminder}
            {NavigationReminder}
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
    /// <param name="navigationMap">
    /// The whole map, every time — it is NOT routed like the documentation. "Where do I do this?" is
    /// not a question a subset can be chosen for in advance, and at a few hundred tokens it does not
    /// need to be. Step 1, the router, never sees it and its budget is untouched.
    /// </param>
    public static IReadOnlyList<ChatMessage> Build(
        IReadOnlyList<AssistantMessage> history,
        int maxHistory,
        string routedDocumentation,
        bool documentationAvailable,
        string navigationMap = "",
        string toolData = "")
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
            new(ChatMessage.SystemRole,
                BuildSystemMessage(routedDocumentation, documentationAvailable, navigationMap, toolData)),
        };

        messages.AddRange(usable.Select(m => new ChatMessage(
            m.Role == AssistantMessageRole.Assistant ? ChatMessage.AssistantRole : ChatMessage.UserRole,
            m.Content)));

        return messages;
    }
}
