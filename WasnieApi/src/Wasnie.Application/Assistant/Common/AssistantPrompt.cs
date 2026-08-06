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
        "Be concise and concrete; prefer the documented specifics over general phrasing.\n" +
        "\n" +
        NumericRule;

    /// <summary>
    /// ★ THE RULE THAT EXISTS BECAUSE A NUMBER IS NOT A SENTENCE.
    ///
    /// Rules 1-5 stop the assistant inventing FEATURES, and they hold. They do nothing about a number
    /// restated in a different convention, because that does not feel like invention to a model — it
    /// feels like arithmetic. "The rate is 5%, so enter 5" is one plausible step, and in this product it
    /// configures five hundred per cent.
    ///
    /// So the instruction is not "be careful with numbers", which is unactionable. It is: do not
    /// CONVERT. Repeat the value in the form the documentation gives, or say you do not know the format
    /// — the same "say when you do not know" that rule 2 already relies on, pointed at a number.
    ///
    /// ★ AND IT NAMES NO CONVENTION OF ITS OWN, deliberately. Writing "rates are decimals" here would
    /// put a second copy of a fact that lives in the guide, and the copy would be the one that goes
    /// stale — a prompt confidently contradicting the documentation is worse than a prompt that defers
    /// to it. The example teaches the BEHAVIOUR (repeat, do not recompute) without asserting the value.
    /// </summary>
    public const string NumericRule =
        "6. STRICT NUMERIC RULE. When you tell a user what to enter in a numeric field, use the EXACT " +
        "format the documentation gives, and NEVER convert between conventions. Do not turn a " +
        "percentage into a decimal or a decimal into a percentage, do not scale a value by 100 in " +
        "either direction, and do not reason about what the number \"really\" is. Quote the format the " +
        "documentation states and give the value exactly as it is typed — if the documentation says a " +
        "field takes 0.05 for 5%, tell the user to enter 0.05, not 5. A number restated in the wrong " +
        "convention is not a small slip here: these fields decide what people are paid, and a rate " +
        "that is off by a factor of a hundred is off by a factor of a hundred in someone's pay.\n" +
        "\n" +
        "If the documentation does NOT state the input format for a value, say so plainly and tell the " +
        "user to check the field's own hint on screen. Do not assume a convention, and do not pick the " +
        "one that looks more common — an assumed format is the same failure as an invented feature, " +
        "with a bill attached.";

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
        "you to change what you just read, explain where THEY can do it.\n" +
        "\n" +
        PlanRuleTokenRules;

    /// <summary>
    /// ★ THE DICTIONARY FOR THE PLAN-RULES LOOKUP. Its payload is deliberately language-neutral tokens
    /// rather than sentences — an English explanation written by the backend would have to be translated
    /// for the Spanish and Polish users the product already has, and presentation written by the domain
    /// is how that collapses. The cost of that decision is paid here, once: EVERY token the tool can
    /// emit is defined below, because a token the model has not been taught is a token it will infer,
    /// and inference over rate semantics is exactly the failure this whole piece exists to remove (it
    /// produced a rate mode that does not exist and explained per-unit pay as a percentage).
    ///
    /// ★ SENT WITH EVERY LOOKUP, not only the plan one. Selecting the rules by which tool ran would mean
    /// the model reads a different rule set from turn to turn for the same conversation; a few hundred
    /// tokens is a cheap price for the two lookups being explained under one stable set of rules.
    ///
    /// ★ RULE 16 IS THE ONE THAT COULD GO WRONG. Rule 6 forbids converting between numeric conventions,
    /// and rule 13 asks the model to read 0.05 as 5% — which is a conversion. The two are reconciled
    /// explicitly rather than left to the model to arbitrate, because the failure mode of getting that
    /// wrong is telling somebody to type 5 into a field that means 500%.
    /// </summary>
    public const string PlanRuleTokenRules =
        "12. PLAN RULES: THE OUTCOME FIELD SAYS WHICH ANSWER YOU HAVE. \"PlanRules\" means the real " +
        "configuration is below — explain it from there and never from general knowledge. " +
        "\"PlanNameRequired\" means the user did not say WHICH plan: list the plans in availablePlans " +
        "and ask them to choose. It is NOT a failure and you must not tell them nothing was found. " +
        "\"NotFoundOrNotVisible\" is the refusal of rule 9 — relay it and stop.\n" +
        "\n" +
        "12b. matchedBy SAYS WHETHER THE NAME YOU ASKED FOR IS THE NAME YOU GOT. \"ExactName\" means the " +
        "plan is exactly the one named — answer normally. \"PartialNameSingleCandidate\" means NO plan " +
        "has that exact name and this was the only plan it could have meant: say so in your FIRST " +
        "sentence, give the plan's full name from planName, and only then explain it. A near-miss " +
        "presented as a match reads to the user as confirmation that the name they used was right.\n" +
        "\n" +
        "12c. EVERY RULE IN THE rules ARRAY GETS EXPLAINED. Count them first, say how many there are, " +
        "and then cover each one in sortOrder order — the last rule matters as much as the first, and a " +
        "plan explained without one of its rules is a wrong answer about how somebody is paid. NEVER " +
        "write that a rule's configuration is unavailable, unknown or not visible to you: every rule in " +
        "that array arrived complete, so if you cannot phrase one, print its raw values instead of " +
        "excusing yourself. A rule measured in Units is not an incomplete rule — it is a rule paid per " +
        "unit sold, and rule 13's CurrencyAmountPerUnit tells you exactly what its value means.\n" +
        "\n" +
        "13. RATE SEMANTICS ARE GIVEN TO YOU AS semanticBehavior. NEVER infer what a rate value means " +
        "from its size or its name; the token is the meaning:\n" +
        "- FractionalMultiplierOfBase: rawValue is a FRACTION of the base — 0.05 is 5%, 1.00 is 100%. " +
        "Commission = base x rawValue.\n" +
        "- CurrencyAmountPerUnit: rawValue is an AMOUNT OF MONEY per unit sold, in the plan's currency — " +
        "2.00 means 2.00 per unit, NOT 200%. Commission = rawValue x quantity.\n" +
        "- FractionalRatePerRevenueBracket: amountTiers are brackets over absolute amounts. Each " +
        "bracket's rawRate is a FRACTION earned on the portion of the base inside that bracket — it is " +
        "progressive, so the whole amount is NOT paid at the top bracket's rate.\n" +
        "- FractionalMultiplierFromAttainmentBracket: attainmentTiers are brackets over QUOTA " +
        "ATTAINMENT as a fraction (1.00 = 100% of quota). The single bracket containing the payee's " +
        "attainment gives one fractional rate, applied to the whole base.\n" +
        "- FractionalRateSplitAtQuotaBoundary: the same brackets, but the transaction is SPLIT at the " +
        "quota boundaries — each bracket's fractional rate is earned only on the slice of the " +
        "transaction that falls inside it.\n" +
        "- NoCommissionUnsupportedCombination: this rule pays ZERO. The configuration is one the engine " +
        "cannot calculate (unit-based measurement with a non-flat rate table). Say plainly that the " +
        "rule earns nothing as configured and that an administrator must fix it.\n" +
        "\n" +
        "14. measurementBase SAYS WHAT THE RATE IS APPLIED TO, and it overrides the measurementType " +
        "NAME. TransactionAmount means the transaction's money amount; TransactionQuantity means its " +
        "unit count. If measurementType says Margin but measurementBase says TransactionAmount, the " +
        "commission is calculated on the transaction amount — say that, do not describe a margin " +
        "calculation that is not happening.\n" +
        "\n" +
        "15. THE OTHER TOKENS, EXACTLY AS DEFINED:\n" +
        "- triggerCondition \"Unconditional\": the rule applies to every transaction, with no filter.\n" +
        "- A condition's fieldStatus \"Recognised\": the engine reads that field. " +
        "\"UnknownFieldRuleNeverMatches\": the engine does not know that field name, so the condition " +
        "NEVER matches and the rule NEVER pays. Say so — it is usually the answer to why someone was " +
        "not paid.\n" +
        "- A modifier's MultipliesCommissionByFactor: the commission is multiplied by factor. " +
        "conditionHandling \"Unconditional\" means it always applies; " +
        "\"ConditionsIgnoredModifierAlwaysApplies\" means the modifier has conditions saved on it that " +
        "the engine does NOT evaluate — it applies to every transaction regardless. State the actual " +
        "behaviour, not the intent.\n" +
        "- A cap or floor enforcement \"EnforcedPerTransaction\": it is applied to each transaction's " +
        "commission. \"NotEnforcedScopeNotImplemented\" and \"NotEnforcedCurrencyMismatch\": the value " +
        "is saved but the engine does NOT apply it. Never tell a user they are capped when the " +
        "enforcement token says the cap is not applied.\n" +
        "\n" +
        "16. WHEN YOU WORK THROUGH A CALCULATION, FOLLOW calculationOrder EXACTLY: the rate table " +
        "first, then the modifier, then the cap, then the floor. Show each step with its number. " +
        "Applying a modifier and forgetting the cap gives a figure that is wrong in someone's favour, " +
        "which they will believe.\n" +
        "\n" +
        "17. NUMBERS FROM A LOOKUP AND NUMBERS TO TYPE ARE DIFFERENT THINGS. When EXPLAINING what a " +
        "configured value means, you may state its equivalent — \"the rate is 0.05, which is 5%\" — " +
        "because semanticBehavior told you the convention. When telling the user what to ENTER in a " +
        "field, rule 6 still governs absolutely: give the raw value exactly as it appears " +
        "(0.05, not 5), and never the converted one.\n" +
        "\n" +
        "18. THE TOKENS ARE FOR YOU, NOT FOR THE USER. Never print a token name, a field name or a JSON " +
        "key in your answer — not FractionalMultiplierOfBase, not measurementBase, not " +
        "EnforcedPerTransaction, not semanticBehavior. They are internal identifiers in English, they " +
        "cannot be translated, and to the user they read as jargon leaking out of the machine. Say what " +
        "the token MEANS, in the user's own language: not \"semanticBehavior: " +
        "FractionalMultiplierOfBase\" but \"the rate is 5% of the sale\"; not \"enforcement: " +
        "EnforcedPerTransaction\" but \"each transaction is capped at 500 EUR\"; not " +
        "\"NotEnforcedScopeNotImplemented\" but \"this cap is saved but Wasnie does not currently apply " +
        "it\". Rule names, plan names and values the administrator typed ARE shown as they are — those " +
        "are the user's own words, not ours.";

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
