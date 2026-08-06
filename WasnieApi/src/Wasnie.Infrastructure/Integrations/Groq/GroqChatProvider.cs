using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Options;
using Wasnie.Infrastructure.Integrations.OpenAiCompatible;

namespace Wasnie.Infrastructure.Integrations.Groq;

/// <summary>
/// Groq's OpenAI-compatible chat completions endpoint.
///
/// ★ WHAT IS LEFT HERE IS THE WHOLE OF WHAT "GROQ" MEANS to this application: a base URL, a key, a
/// model id and a named HTTP client. The protocol — SSE streaming, the `[DONE]` sentinel, JSON mode,
/// tool calling, the error translation — lives in <see cref="OpenAiCompatibleChatProvider"/>, because
/// none of it is Groq-specific and a second vendor proved it.
///
/// ★ STILL AVAILABLE, NOT REPLACED. OpenRouter was added because Groq has no purchasable tier and its
/// free allowance is a permanent wall, not because Groq is worse. Which one runs is a configuration
/// value; this class is one of the two answers, and deleting it would throw away a working provider to
/// save a file.
/// </summary>
public sealed class GroqChatProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<GroqOptions> options,
    ILogger<GroqChatProvider> logger)
    : OpenAiCompatibleChatProvider(httpClientFactory, logger)
{
    /// <summary>Named client, matching how Resend and HubSpot are wired.</summary>
    public const string HttpClientName = "Groq";

    protected override OpenAiCompatibleSettings Settings { get; } = new(
        options.Value.ApiKey,
        options.Value.BaseUrl,
        options.Value.Model,
        // Empty means "not configured separately" — the generation call then uses the same model as
        // before, so an appsettings that predates the split keeps behaving exactly as it did.
        string.IsNullOrWhiteSpace(options.Value.GenerationModel)
            ? options.Value.Model
            : options.Value.GenerationModel,
        options.Value.TimeoutSeconds,
        HttpClientName);
}
