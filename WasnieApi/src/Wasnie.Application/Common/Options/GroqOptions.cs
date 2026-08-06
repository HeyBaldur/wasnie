namespace Wasnie.Application.Common.Options;

/// <summary>
/// Configuration for the chat model.
///
/// ★ THE KEY IS SERVER-SIDE ONLY. It is read from configuration (appsettings.Development.json, which is
/// gitignored, or the environment in a deployed setting), it is never sent to the browser, never logged
/// and never part of any DTO. The browser talks to Wasnie; Wasnie talks to the model. One key, held by
/// the operator, covering every tenant — the cost rides on the subscription.
///
/// Bound WITHOUT ValidateOnStart, like HubSpot: the application must still start for everyone who has
/// not configured a model, and the assistant degrades to its stand-in reply instead of taking the API
/// down with it.
/// </summary>
public sealed class GroqOptions
{
    public const string SectionName = "Groq";

    public string ApiKey { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = "https://api.groq.com/openai/v1";

    /// <summary>
    /// The API model id — the string Groq's API expects, NOT the commercial name. "openai/gpt-oss-20b" is
    /// marketing; `openai/gpt-oss-20b` is what the endpoint answers to, and a wrong id is a 404 on
    /// every call. Verified against GET /v1/models rather than guessed.
    ///
    /// ★ USED FOR BOTH STEPS — routing and generation. The 20B is roughly eight times cheaper on input
    /// than the 70B, which is what makes the free tier's 100,000-tokens-a-day budget survive a day of
    /// testing; the 70B exhausted it. Verified before switching: this model supports both the strict
    /// JSON mode the router depends on and the streaming the generation depends on.
    /// </summary>
    public string Model { get; init; } = "openai/gpt-oss-20b";

    /// <summary>
    /// The model that WRITES THE ANSWER — the only output a user reads. Falls back to
    /// <see cref="Model"/> when unset, so an existing configuration keeps working unchanged.
    ///
    /// ★ WHY IT IS NOT <see cref="Model"/>. gpt-oss-20b fell into a repetition loop while explaining a
    /// three-rule plan — "mandatorio mandatorio mandatorio" for hundreds of words, on screen, in a
    /// product that tells people what they are paid. It is a property of a small model under a long
    /// structured task, not something a prompt fixes. The router and the tool dispatcher are
    /// classification and stay cheap; generation buys robustness where it is actually read.
    /// </summary>
    public string GenerationModel { get; init; } = "openai/gpt-oss-120b";

    /// <summary>
    /// Ceiling on how much conversation is replayed to the model, in turns. A long thread would
    /// otherwise grow the request without bound — cost and latency both rise with it, and the oldest
    /// turns contribute least. Newest turns are the ones kept.
    /// </summary>
    public int MaxHistoryMessages { get; init; } = 20;

    public int TimeoutSeconds { get; init; } = 60;
}
