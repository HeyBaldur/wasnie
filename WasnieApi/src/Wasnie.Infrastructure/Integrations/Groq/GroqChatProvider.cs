using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Common.Options;

namespace Wasnie.Infrastructure.Integrations.Groq;

/// <summary>
/// The one implementation of <see cref="IChatCompletionProvider"/>: Groq's OpenAI-compatible chat
/// completions endpoint, consumed as a Server-Sent Events stream.
///
/// ★ THE KEY NEVER LEAVES THIS CLASS. It is read from options, attached as a request header, and that
/// is the whole of its travel. It is not returned, not logged (the logging below records status codes
/// and reason keys, never the header, never the body), and no DTO in the system has a field it could
/// occupy.
///
/// ★ VENDOR ERRORS ARE TRANSLATED, NOT FORWARDED. Everything that goes wrong leaves here as a
/// <see cref="ChatCompletionException"/> carrying a translation KEY. A vendor's own error text can
/// contain request ids, model names and fragments of the prompt; handing it to the browser would leak
/// operational detail to whoever asked a question at the wrong moment.
/// </summary>
public sealed class GroqChatProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<GroqOptions> options,
    ILogger<GroqChatProvider> logger)
    : IChatCompletionProvider
{
    private readonly GroqOptions _options = options.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new ChatCompletionException(
                ChatCompletionException.NotConfigured, "No chat model API key is configured.");
        }

        using var request = BuildRequest(messages, stream: true);

        var client = httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        HttpResponseMessage response;
        try
        {
            // ResponseHeadersRead: the point of streaming is to start reading before the body is
            // finished. The default would buffer the whole completion and hand it over at the end,
            // which is exactly the wait this feature exists to remove.
            response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The token is fine, so this is the HttpClient timeout — a slow model, not a user leaving.
            throw new ChatCompletionException(
                ChatCompletionException.Unavailable, "The chat model timed out.");
        }
        catch (HttpRequestException ex)
        {
            throw new ChatCompletionException(
                ChatCompletionException.Unavailable, "The chat model is unreachable.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await TranslateFailureAsync(response, cancellationToken);
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(body, Encoding.UTF8);

            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                // SSE: blank lines separate events, and only `data:` lines carry payload.
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                var payload = line[5..].Trim();

                // The sentinel that ends an OpenAI-compatible stream.
                if (payload is "[DONE]")
                {
                    yield break;
                }

                var fragment = ReadFragment(payload);
                if (!string.IsNullOrEmpty(fragment))
                {
                    yield return fragment;
                }
            }
        }
    }

    /// <summary>Named client, matching how Resend and HubSpot are wired.</summary>
    public const string HttpClientName = "Groq";

    private HttpRequestMessage BuildRequest(
        IReadOnlyList<ChatMessage> messages, bool stream, bool jsonObject = false)
    {
        var payload = new GroqRequest(
            Model: _options.Model,
            Stream: stream,
            Messages: messages.Select(m => new GroqRequestMessage(m.Role, m.Content)).ToList(),
            ResponseFormat: jsonObject ? new GroqResponseFormat("json_object") : null);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        return request;
    }

    /// <summary>
    /// One fragment out of one SSE payload. A malformed chunk is skipped rather than thrown on: a
    /// single unparseable frame mid-answer should not discard the answer around it.
    /// </summary>
    private static string? ReadFragment(string payload)
    {
        try
        {
            var chunk = JsonSerializer.Deserialize<GroqStreamChunk>(payload, JsonOptions);
            return chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<ChatCompletionException> TranslateFailureAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // ★ LOGGED FOR THE OPERATOR, never forwarded to the user. The first version discarded this
        // body entirely, and the cost was immediate: a 413 arrived in production and the log said only
        // "413", so diagnosing it meant guessing. The vendor's message said exactly what was wrong
        // ("Request too large … tokens per minute"). The rule was always "the USER must not see it" —
        // silencing the OPERATOR too was an over-correction that made a solvable problem opaque.
        // Truncated because a refusal can echo back a slice of the prompt, and a log is not a place to
        // spill fifteen thousand tokens of documentation.
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var excerpt = body.Length > 500 ? body[..500] : body;

        var reasonKey = response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => ChatCompletionException.RateLimited,
            // ★ `tool_use_failed`: the model wrote a tool call the provider's own validator rejected.
            // Recognised by the body's error code because the status alone (400) says nothing — a 400
            // is also a malformed request, which is our bug and not the model's. Separated so the
            // caller can shrug it off and answer without the lookup, and so the log distinguishes
            // "the model fumbled one call" from "the provider is down".
            HttpStatusCode.BadRequest when excerpt.Contains("tool_use_failed", StringComparison.OrdinalIgnoreCase)
                => ChatCompletionException.ToolCallRejected,
            // 413 from Groq is not "your upload is too big" in the usual sense — it is the
            // tokens-per-minute allowance refusing the request. It IS a rate limit, so the user is
            // told to try again in a moment rather than handed a generic failure.
            HttpStatusCode.RequestEntityTooLarge => ChatCompletionException.RateLimited,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ChatCompletionException.NotConfigured,
            _ => ChatCompletionException.Unavailable,
        };

        logger.LogWarning(
            "Chat completion refused by the provider with status {StatusCode}; surfaced as {ReasonKey}. Provider said: {ProviderMessage}",
            (int)response.StatusCode, reasonKey, excerpt);

        return new ChatCompletionException(
            reasonKey, $"The chat model refused the request ({(int)response.StatusCode}).");
    }

    public async Task<string> CompleteJsonAsync(
        IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new ChatCompletionException(
                ChatCompletionException.NotConfigured, "No chat model API key is configured.");
        }

        using var request = BuildRequest(messages, stream: false, jsonObject: true);

        var client = httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatCompletionException(
                ChatCompletionException.Unavailable, "The chat model timed out.");
        }
        catch (HttpRequestException ex)
        {
            throw new ChatCompletionException(
                ChatCompletionException.Unavailable, "The chat model is unreachable.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await TranslateFailureAsync(response, cancellationToken);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            try
            {
                var completion = JsonSerializer.Deserialize<GroqCompletion>(body, JsonOptions);
                return completion?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
            }
            catch (JsonException ex)
            {
                // The envelope itself was malformed — a different failure from the CONTENT being
                // unparseable, which is the caller's problem to handle.
                throw new ChatCompletionException(
                    ChatCompletionException.Unavailable, "The chat model returned an unreadable response.", ex);
            }
        }
    }

    /// <summary>
    /// Asks the model which tool to run, if any.
    ///
    /// ★ VERIFIED AGAINST THE REAL API, not assumed. `openai/gpt-oss-20b` — the configured model —
    /// returns a `tool_calls` array for "What happened with transaction TERM-CC-10?" with the reference
    /// correctly extracted. Not every model on the platform does tool calling, and shipping this on the
    /// assumption that ours did would have failed silently as "the assistant just never looks anything
    /// up".
    ///
    /// `tool_choice: auto` rather than forced: the model must be free to answer a documentation
    /// question without a lookup, which is most questions.
    ///
    /// One call is returned even when the model asks for several. A single read-only lookup is the
    /// whole of this piece; taking the first is honest about that, where quietly running all of them
    /// would be a loop nobody designed.
    /// </summary>
    public async Task<AssistantToolRequest?> SelectToolAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<AssistantToolSchema> tools,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new ChatCompletionException(
                ChatCompletionException.NotConfigured, "No chat model API key is configured.");
        }

        if (tools.Count == 0)
        {
            return null;
        }

        var payload = new GroqRequest(
            Model: _options.Model,
            Stream: false,
            Messages: messages.Select(m => new GroqRequestMessage(m.Role, m.Content)).ToList(),
            Tools: tools.Select(t => new GroqTool(
                "function",
                new GroqFunction(t.Name, t.Description, JsonSerializer.Deserialize<JsonElement>(t.ParametersJson))))
                .ToList(),
            ToolChoice: "auto");

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var client = httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatCompletionException(
                ChatCompletionException.Unavailable, "The chat model timed out.");
        }
        catch (HttpRequestException ex)
        {
            throw new ChatCompletionException(
                ChatCompletionException.Unavailable, "The chat model is unreachable.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await TranslateFailureAsync(response, cancellationToken);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            try
            {
                var call = JsonSerializer.Deserialize<GroqCompletion>(body, JsonOptions)
                    ?.Choices?.FirstOrDefault()?.Message?.ToolCalls?.FirstOrDefault();

                return call?.Function is null || string.IsNullOrWhiteSpace(call.Function.Name)
                    ? null
                    : new AssistantToolRequest(call.Function.Name, call.Function.Arguments ?? "{}");
            }
            catch (JsonException ex)
            {
                throw new ChatCompletionException(
                    ChatCompletionException.Unavailable, "The chat model returned an unreadable response.", ex);
            }
        }
    }

    // ── Wire shapes, private on purpose: nothing outside this file models the vendor ──

    private sealed record GroqRequest(
        string Model,
        bool Stream,
        IReadOnlyList<GroqRequestMessage> Messages,
        GroqResponseFormat? ResponseFormat = null,
        IReadOnlyList<GroqTool>? Tools = null,
        string? ToolChoice = null);

    private sealed record GroqTool(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("function")] GroqFunction Function);

    private sealed record GroqFunction(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("parameters")] JsonElement Parameters);

    private sealed record GroqToolCall(
        [property: JsonPropertyName("function")] GroqToolCallFunction? Function);

    private sealed record GroqToolCallFunction(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("arguments")] string? Arguments);

    /// <summary>The provider's structured-output switch. `json_object` makes a non-JSON reply impossible.</summary>
    private sealed record GroqResponseFormat(string Type);

    private sealed record GroqCompletion(
        [property: JsonPropertyName("choices")] IReadOnlyList<GroqCompletionChoice>? Choices);

    private sealed record GroqCompletionChoice(
        [property: JsonPropertyName("message")] GroqCompletionMessage? Message);

    private sealed record GroqCompletionMessage(
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("tool_calls")] IReadOnlyList<GroqToolCall>? ToolCalls = null);

    private sealed record GroqRequestMessage(string Role, string Content);

    private sealed record GroqStreamChunk(
        [property: JsonPropertyName("choices")] IReadOnlyList<GroqChoice>? Choices);

    private sealed record GroqChoice(
        [property: JsonPropertyName("delta")] GroqDelta? Delta);

    private sealed record GroqDelta(
        [property: JsonPropertyName("content")] string? Content);
}
