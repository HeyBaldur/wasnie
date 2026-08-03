namespace Wasnie.Application.Assistant.Abstractions;

/// <summary>One turn as the model sees it. Deliberately not the domain entity — a provider knows nothing about tenants, conversations or sequences.</summary>
public sealed record ChatMessage(string Role, string Content)
{
    public const string SystemRole = "system";
    public const string UserRole = "user";
    public const string AssistantRole = "assistant";
}

/// <summary>
/// A chat model that can continue a conversation.
///
/// ★ THE INTERFACE NAMES NO VENDOR, and that is the whole point of it existing. There is exactly ONE
/// implementation today (Groq) and this is NOT a multi-provider framework: there is no registry, no
/// per-tenant selection and no fallback chain, because there is no second provider and inventing the
/// machinery for one would be building for a problem nobody has. What the interface buys is that the
/// handler, and the retrieval work coming after it, never learn which vendor answers — so adding a
/// second one later is a new class and a changed registration, not a rewrite of the chat.
///
/// ★ HONEST ABOUT ITS LIMITS: this covers sending messages and receiving streamed text, which really
/// is portable. Tool calling is NOT modelled here — it differs enough between vendors that a shared
/// abstraction written before a second vendor exists would be a guess. When that piece arrives it gets
/// its own design.
/// </summary>
public interface IChatCompletionProvider
{
    /// <summary>
    /// False when no API key is configured. The chat then falls back to the stand-in reply rather than
    /// failing, so a developer without a key still gets a working panel — the same graceful-skip the
    /// email service uses when Resend is unconfigured.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Streams the assistant's answer in fragments, in order.
    ///
    /// Throws <see cref="ChatCompletionException"/> for anything the caller should turn into a user-
    /// facing error (transport failure, rate limit, rejected key, malformed response). Callers must
    /// assume a stream can fail PART WAY THROUGH, after some fragments were already yielded.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken);

    /// <summary>
    /// One completion, collected, with the provider constrained to return a JSON object.
    ///
    /// ★ THE CONSTRAINT IS THE POINT. Asked politely for JSON, a model will sometimes reply "Sure!
    /// Here is the JSON:" and wrap it in a code fence — which parses to nothing and takes the feature
    /// down. The provider's own structured-output mode makes that impossible rather than unlikely.
    ///
    /// Not streamed: the caller needs the whole object before it can do anything with it, so there is
    /// nothing for fragments to be good for.
    ///
    /// Throws <see cref="ChatCompletionException"/> on the same failures as <see cref="StreamAsync"/>.
    /// </summary>
    Task<string> CompleteJsonAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken);

    /// <summary>
    /// Asks the model whether one of <paramref name="tools"/> should be run for this question, and
    /// returns the one it chose — or null when it wants no tool.
    /// </summary>
    /// <remarks>
    /// ★ THE SEAM IS "WHICH TOOL", NOT "RUN THE CONVERSATION". The file's own note used to say tool
    /// calling was not modelled here because it differs too much between vendors to abstract before a
    /// second one exists. That is still true of the FULL protocol — the assistant/tool message roles,
    /// the call ids, the multi-round loop — so none of it is abstracted. What IS portable is the one
    /// question this method asks: given these messages and these tools, which tool and with what
    /// arguments? Every vendor answers that; only the wire shape differs, and that stays inside the
    /// implementation.
    ///
    /// ★ THE RESULT IS NOT FED BACK AS A `tool` ROLE MESSAGE. It is handed to the generating call as
    /// clearly delimited data (see AssistantPrompt). That keeps <see cref="ChatMessage"/> free of call
    /// ids and tool roles — vendor concepts the rest of the application would then have to carry — at
    /// the cost of one round of tool use per turn, which is all a single read-only lookup needs.
    ///
    /// Returns null when the model wants no tool. Throws <see cref="ChatCompletionException"/> on the
    /// same failures as the other methods.
    /// </remarks>
    Task<AssistantToolRequest?> SelectToolAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<AssistantToolSchema> tools,
        CancellationToken cancellationToken);
}

/// <summary>
/// A chat completion could not be produced.
///
/// <see cref="ReasonKey"/> is a translation key, not a sentence: the message the user reads is rendered
/// by the client in their own language. The provider's own words (and anything it echoed back) never
/// reach the user — a vendor error string can carry request ids, model names and, in the worst case,
/// fragments of the request.
/// </summary>
public sealed class ChatCompletionException(string reasonKey, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string ReasonKey { get; } = reasonKey;

    /// <summary>The provider is unreachable, timed out, or returned something unusable.</summary>
    public const string Unavailable = "ASSISTANT.ERROR_UNAVAILABLE";

    /// <summary>Too many requests — the one case where "try again in a moment" is real advice.</summary>
    public const string RateLimited = "ASSISTANT.ERROR_RATE_LIMITED";

    /// <summary>The key is missing, wrong or rejected. An operator problem, phrased for a user.</summary>
    public const string NotConfigured = "ASSISTANT.ERROR_NOT_CONFIGURED";

    /// <summary>
    /// The model produced a tool call the provider itself rejected (`400 tool_use_failed`).
    ///
    /// ★ ITS OWN REASON, NOT "UNAVAILABLE". This is a generation-quality failure — the model wrote a
    /// call that did not validate — and it is recoverable by simply answering without the lookup. Filed
    /// under the same key as "the provider is unreachable" it would read, to whoever is on the logs, as
    /// an outage; and the two want opposite responses. It never reaches a user: the tool runner treats
    /// it as "no tool was chosen".
    /// </summary>
    public const string ToolCallRejected = "ASSISTANT.ERROR_TOOL_CALL_REJECTED";
}
