namespace Wasnie.Application.Assistant.Abstractions;

/// <summary>
/// The app's screens and the routes that reach them, as a block the model can be handed.
///
/// ★ WHY THIS IS NOT PART OF THE DOCUMENTATION. <see cref="IAssistantKnowledgeBase"/> carries BUSINESS
/// RULES — what a clawback is, when a plan may be edited. This carries ROUTING — that creating a plan
/// happens at `/plans/new`. They change for different reasons: a UI redesign rewrites every route and
/// not one rule, and putting URLs in the handbook would mean a screen rename dirties the document the
/// team publishes to customers. Two artefacts, two lifecycles.
///
/// ★ WHY IT EXISTS AT ALL. Asked to guide someone, a model produces a URL that LOOKS right —
/// `/admin/create-plan` reads perfectly and is a 404. It has no way to know Wasnie's real routes unless
/// it is told them, and a broken link in a step-by-step guide is worse than no link: it breaks trust at
/// the exact moment the user finally acted on the advice.
///
/// ★ FIXED CONTEXT, AND SMALL. Unlike the documentation this is NOT routed — the whole map goes into
/// every generating call, because the question "where do I do this?" cannot be answered by choosing a
/// subset in advance. It is a few hundred tokens, which is what makes that affordable; the router
/// (step 1) never sees it and its budget is untouched.
/// </summary>
public interface IUiNavigationMap
{
    /// <summary>
    /// The map rendered for the prompt, or empty when the file could not be read.
    ///
    /// Empty is degraded, not fatal: the assistant keeps explaining, it just stops giving links —
    /// and the no-invented-routes rule means it stops giving links rather than starting to guess them.
    /// </summary>
    string PromptBlock { get; }

    /// <summary>False when the map could not be loaded. The assistant then guides without links.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Every route the assistant is allowed to emit. Exposed so a test can assert that what reaches
    /// the model is what the file says, rather than trusting a rendered string to have carried it.
    /// </summary>
    IReadOnlyList<string> Routes { get; }
}
