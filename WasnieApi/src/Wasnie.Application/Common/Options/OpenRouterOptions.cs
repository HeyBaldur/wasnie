namespace Wasnie.Application.Common.Options;

/// <summary>
/// Configuration for the OpenRouter chat model.
///
/// ★ WHY A SECOND PROVIDER EXISTS AT ALL. Groq's free allowance — 8,000 tokens a minute — is a
/// permanent ceiling, not a starting one: its Developer tier cannot be purchased. Tool calling made
/// every turn cost more, and the assistant started hitting 429 in ordinary use. OpenRouter is an
/// aggregator speaking the same OpenAI protocol, billed per use, with no free-tier wall.
///
/// ★ THE KEY IS SERVER-SIDE ONLY, exactly as Groq's: read from configuration, attached to one outbound
/// header inside the provider, never sent to the browser, never logged, and no DTO has a field it
/// could occupy. There is a test that walks every client-facing type to keep that true.
///
/// Bound WITHOUT ValidateOnStart, like Groq and HubSpot: the application must start for anyone who has
/// not configured a model, and the assistant degrades to its stand-in reply rather than taking the API
/// down.
/// </summary>
public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string ApiKey { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = "https://openrouter.ai/api/v1";

    /// <summary>
    /// The API model id, verified against OpenRouter's own catalogue (GET /api/v1/models) rather than
    /// guessed — a wrong id is a failure on every call, and the same mistake cost a release on Groq.
    ///
    /// ★ THE SAME MODEL WE ALREADY RUN. `openai/gpt-oss-20b` is the id on both platforms, so switching
    /// provider changes who serves the model and not which model answers — the routing prompts, the
    /// tool schema and the confinement rules were all tuned against this one, and swapping the model at
    /// the same time as the vendor would make any change in behaviour impossible to attribute.
    ///
    /// The catalogue lists `response_format`, `structured_outputs`, `tools` and `tool_choice` among its
    /// supported parameters — which is JSON mode for the router and tool calling for piece 5, the two
    /// things this assistant cannot work without.
    ///
    /// ★ NOT the `:free` variant. OpenRouter offers `openai/gpt-oss-20b:free` at zero cost, and it
    /// carries its own rate limits — the exact wall this provider exists to get out from behind.
    /// Paying fractions of a cent to stop hitting 429 is the entire point.
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
    /// Sent as OpenRouter's optional attribution headers (`HTTP-Referer`, `X-Title`). They identify the
    /// calling application on OpenRouter's own dashboards; they carry no secret and no user data.
    /// </summary>
    public string AppName { get; init; } = "Incentra";

    public string AppUrl { get; init; } = "https://incentra.work";

    /// <summary>Same meaning as <see cref="GroqOptions.MaxHistoryMessages"/> — kept per provider so a
    /// cheaper endpoint can afford a longer thread without editing the other one.</summary>
    public int MaxHistoryMessages { get; init; } = 20;

    public int TimeoutSeconds { get; init; } = 60;
}

/// <summary>
/// Which chat provider answers.
///
/// ★ A CONFIGURATION VALUE, NOT A CODE CHANGE. Both providers implement the same interface, so the
/// choice is a registration detail — and making it a setting means switching back to Groq (or forward
/// to a third vendor) is an appsettings edit and a restart, at the moment somebody needs it, rather
/// than a deployment.
/// </summary>
public sealed class AssistantProviderOptions
{
    public const string SectionName = "Assistant";

    public const string Groq = "Groq";
    public const string OpenRouter = "OpenRouter";

    /// <summary>
    /// `Groq` or `OpenRouter`. Anything unrecognised falls back to Groq with a warning rather than
    /// throwing: a typo here must not take the API down, and the assistant degrading to a provider that
    /// works is better than a start-up failure for a chat panel.
    /// </summary>
    public string Provider { get; init; } = Groq;
}
