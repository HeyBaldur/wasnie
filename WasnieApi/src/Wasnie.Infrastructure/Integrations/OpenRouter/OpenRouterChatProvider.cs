using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Options;
using Wasnie.Infrastructure.Integrations.OpenAiCompatible;

namespace Wasnie.Infrastructure.Integrations.OpenRouter;

/// <summary>
/// OpenRouter's chat completions endpoint — an aggregator in front of many vendors, speaking OpenAI's
/// protocol.
///
/// ★ HOW LITTLE THERE IS HERE IS THE POINT. Streaming, JSON mode, tool calling and error translation
/// are inherited unchanged, because OpenRouter answers to the same shapes Groq does. If this class had
/// needed to reimplement any of that, the abstraction would have been the wrong one.
///
/// ★ WHY IT WAS ADDED. Groq's free allowance is a ceiling that cannot be raised — its Developer tier
/// is not purchasable — and tool calling pushed ordinary use into 429s. OpenRouter bills per use with
/// no such wall, at roughly three cents per million input tokens for the same model.
///
/// ★ THE KEY NEVER LEAVES THE BASE CLASS. It is attached to one outbound header and travels nowhere
/// else; nothing here logs it and no DTO has a field it could occupy.
/// </summary>
public sealed class OpenRouterChatProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenRouterOptions> options,
    ILogger<OpenRouterChatProvider> logger)
    : OpenAiCompatibleChatProvider(httpClientFactory, logger)
{
    public const string HttpClientName = "OpenRouter";

    private readonly OpenRouterOptions _options = options.Value;

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

    /// <summary>
    /// The key, plus OpenRouter's two optional attribution headers.
    ///
    /// They identify the calling application on OpenRouter's dashboards and carry NO secret and NO user
    /// data — a product name and a public URL. Worth stating explicitly because "extra headers on every
    /// request to a third party" is exactly the kind of thing that should be checked rather than
    /// assumed harmless.
    /// </summary>
    protected override void Authorise(HttpRequestMessage request)
    {
        base.Authorise(request);

        request.Headers.TryAddWithoutValidation("HTTP-Referer", _options.AppUrl);
        request.Headers.TryAddWithoutValidation("X-Title", _options.AppName);
    }
}
