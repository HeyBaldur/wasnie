using System.Net;
using System.Reflection;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Common.Options;
using Wasnie.Infrastructure;
using Wasnie.Infrastructure.Integrations.Groq;
using Wasnie.Infrastructure.Integrations.OpenRouter;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// OpenRouter as a second chat provider behind the SAME interface.
///
/// ★ THESE DRIVE THE REAL PROVIDER OVER A FAKE TRANSPORT, not a mock of the interface. Mocking
/// <see cref="IChatCompletionProvider"/> would prove that something implements it, which nobody
/// doubts. What is worth pinning is the WIRE: that the request carries the configured model and the
/// key, that a streamed body is parsed into fragments, that JSON mode and tools are asked for, and
/// that the vendor's failures arrive as the same translation keys the front already handles. All of
/// that is only visible at the HTTP boundary.
/// </summary>
public sealed class OpenRouterProviderTests
{
    /// <summary>Answers with a scripted response and keeps the request for inspection.</summary>
    private sealed class FakeTransport(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }

    private static HttpResponseMessage Ok(string body, string contentType = "application/json") =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, contentType) };

    private static (OpenRouterChatProvider Provider, FakeTransport Transport) Build(
        HttpResponseMessage response, OpenRouterOptions? options = null)
    {
        var transport = new FakeTransport(response);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(OpenRouterChatProvider.HttpClientName)
            .Returns(_ => new HttpClient(transport, disposeHandler: false));

        var provider = new OpenRouterChatProvider(
            factory,
            Options.Create(options ?? new OpenRouterOptions { ApiKey = "sk-or-test-key" }),
            NullLogger<OpenRouterChatProvider>.Instance);

        return (provider, transport);
    }

    // ── Test 1 — selection by configuration ───────────────────────────────────

    private static IChatCompletionProvider Resolve(string? provider)
    {
        // The real registration, driven by the real configuration key — a hand-rolled switch in the
        // test would prove only that the test can switch.
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=(local);Database=x;Trusted_Connection=True;",
            ["Stripe:SecretKey"] = "sk_test",
            ["Stripe:PublishableKey"] = "pk_test",
            ["Stripe:WebhookSecret"] = "whsec_test",
        };

        if (provider is not null)
        {
            settings[$"{AssistantProviderOptions.SectionName}:{nameof(AssistantProviderOptions.Provider)}"] = provider;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider().CreateScope()
            .ServiceProvider.GetRequiredService<IChatCompletionProvider>();
    }

    [Fact]
    public void The_configured_provider_is_the_one_that_gets_injected()
    {
        // ★ Switching vendor is a settings edit and a restart — not a deployment. The handler never
        // learns which one answered, which is what the interface bought in 2a.
        Resolve("OpenRouter").Should().BeOfType<OpenRouterChatProvider>();
        Resolve("Groq").Should().BeOfType<GroqChatProvider>();

        // Case is not a trap for whoever edits the file.
        Resolve("openrouter").Should().BeOfType<OpenRouterChatProvider>();
    }

    [Fact]
    public void An_absent_or_misspelt_provider_falls_back_to_Groq_rather_than_failing_to_start()
    {
        // ★ A typo in a chat panel's configuration must not stop the API. Everything else in Wasnie —
        // pay runs, ledgers, transactions — has nothing to do with which model answers questions.
        Resolve(null).Should().BeOfType<GroqChatProvider>();
        Resolve("OpenRoutr").Should().BeOfType<GroqChatProvider>();
        Resolve(string.Empty).Should().BeOfType<GroqChatProvider>();
    }

    [Fact]
    public void Groq_is_still_available_and_was_not_replaced()
    {
        // OpenRouter was added because Groq has no purchasable tier — not because Groq stopped working.
        typeof(GroqChatProvider).Should().BeAssignableTo<IChatCompletionProvider>();
        Resolve("Groq").Should().BeOfType<GroqChatProvider>();
    }

    // ── Test 2 — the key does not escape ──────────────────────────────────────

    [Fact]
    public async Task The_key_travels_ONLY_as_an_outbound_header()
    {
        var (provider, transport) = Build(Ok("""{"choices":[{"message":{"content":"hi"}}]}"""));

        await provider.CompleteJsonAsync([new ChatMessage(ChatMessage.UserRole, "json please")], CancellationToken.None);

        transport.Request!.Headers.Authorization!.Parameter.Should().Be("sk-or-test-key");
        // ...and nowhere else. Not in the body the vendor is sent, and not in the URL.
        transport.Body.Should().NotContain("sk-or-test-key");
        transport.Request.RequestUri!.ToString().Should().NotContain("sk-or-test-key");
    }

    [Fact]
    public void No_type_the_client_can_see_has_a_field_the_key_could_occupy()
    {
        // ★ The same reflection sweep that guards the Groq key, extended to this one. A DTO gaining an
        // `ApiKey` is how a server-side secret becomes a browser-side one, and it looks harmless in a
        // diff.
        var clientFacing = typeof(Wasnie.Application.Assistant.DTOs.AssistantMessageDto).Assembly
            .GetTypes()
            .Where(t => t.IsClass && t.Namespace is not null && t.Namespace.Contains(".DTOs", StringComparison.Ordinal))
            .ToList();

        clientFacing.Should().NotBeEmpty("the sweep must actually be looking at something");

        foreach (var type in clientFacing)
        {
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name)
                .Should().NotContain(
                    n => n.Contains("ApiKey", StringComparison.OrdinalIgnoreCase),
                    $"{type.Name} is sent to the browser");
        }

        // The options type holds it and is NOT a DTO — that separation is the whole arrangement.
        typeof(OpenRouterOptions).GetProperty(nameof(OpenRouterOptions.ApiKey)).Should().NotBeNull();
        clientFacing.Should().NotContain(typeof(OpenRouterOptions));
    }

    [Fact]
    public void An_unconfigured_provider_reports_itself_unconfigured_instead_of_calling_out()
    {
        var (provider, _) = Build(Ok("{}"), new OpenRouterOptions { ApiKey = string.Empty });

        provider.IsConfigured.Should().BeFalse("no key means the stand-in reply, not a failed request");
    }

    // ── Test 3 — the three flows over the wire ────────────────────────────────

    [Fact]
    public async Task The_router_asks_for_JSON_MODE_and_gets_its_object_back()
    {
        var (provider, transport) = Build(Ok("""{"choices":[{"message":{"content":"{\"sections\":[\"s4\"]}"}}]}"""));

        var json = await provider.CompleteJsonAsync(
            [new ChatMessage(ChatMessage.SystemRole, "Return ONLY a JSON object.")], CancellationToken.None);

        json.Should().Be("""{"sections":["s4"]}""");

        // ★ The strict mode is what makes a non-JSON reply impossible rather than unlikely. The model
        // catalogue lists response_format among this model's supported parameters.
        transport.Body.Should().Contain("\"response_format\"");
        transport.Body.Should().Contain("json_object");
        transport.Body.Should().Contain("openai/gpt-oss-20b", "the configured model id, verified against the catalogue");
        transport.Request!.RequestUri!.ToString().Should().Be("https://openrouter.ai/api/v1/chat/completions");
    }

    [Fact]
    public async Task Generation_STREAMS_the_answer_in_fragments()
    {
        // A real Server-Sent Events body, including the sentinel that ends an OpenAI-compatible stream.
        var sse = string.Join("\n\n", [
            """data: {"choices":[{"delta":{"content":"It was "}}]}""",
            """data: {"choices":[{"delta":{"content":"paid."}}]}""",
            "data: [DONE]",
        ]) + "\n\n";

        var (provider, transport) = Build(Ok(sse, "text/event-stream"));

        var fragments = new List<string>();
        await foreach (var fragment in provider.StreamAsync(
            [new ChatMessage(ChatMessage.UserRole, "what happened?")], CancellationToken.None))
        {
            fragments.Add(fragment);
        }

        fragments.Should().Equal("It was ", "paid.");
        transport.Body.Should().Contain("\"stream\":true");
    }

    [Fact]
    public async Task Tool_calling_offers_the_schema_and_reads_the_call_back()
    {
        var (provider, transport) = Build(Ok("""
            {"choices":[{"message":{"tool_calls":[
              {"id":"fc_1","type":"function",
               "function":{"name":"get_transaction","arguments":"{\"reference\":\"TERM-CC-10\"}"}}]}}]}
            """));

        var chosen = await provider.SelectToolAsync(
            [new ChatMessage(ChatMessage.UserRole, "what happened with TERM-CC-10?")],
            [new AssistantToolSchema(
                "get_transaction", "Look up one sales transaction. Read-only.",
                """{"type":"object","properties":{"reference":{"type":"string"}},"required":["reference"]}""")],
            CancellationToken.None);

        chosen!.Name.Should().Be("get_transaction");
        chosen.ArgumentsJson.Should().Contain("TERM-CC-10");

        transport.Body.Should().Contain("\"tools\"");
        transport.Body.Should().Contain("\"tool_choice\":\"auto\"");
        transport.Body.Should().Contain("get_transaction");
    }

    [Fact]
    public async Task A_reply_with_no_tool_call_means_no_tool()
    {
        var (provider, _) = Build(Ok("""{"choices":[{"message":{"content":"A plan is..."}}]}"""));

        var chosen = await provider.SelectToolAsync(
            [new ChatMessage(ChatMessage.UserRole, "what is a plan?")],
            [new AssistantToolSchema("get_transaction", "…", """{"type":"object"}""")],
            CancellationToken.None);

        chosen.Should().BeNull("most questions are answered from the documentation");
    }

    [Fact]
    public async Task OpenRouters_attribution_headers_ride_along_and_carry_no_secret()
    {
        var (provider, transport) = Build(Ok("""{"choices":[{"message":{"content":"{}"}}]}"""));

        await provider.CompleteJsonAsync([new ChatMessage(ChatMessage.UserRole, "x")], CancellationToken.None);

        transport.Request!.Headers.GetValues("X-Title").Should().Contain("Incentra");
        transport.Request.Headers.GetValues("HTTP-Referer").Should().NotBeEmpty();
        // A product name and a public URL — nothing about the user, nothing about the key.
        transport.Request.Headers.GetValues("X-Title").Should().NotContain(v => v.Contains("sk-or"));
    }

    // ── Test 4 — failures map to the keys the front already handles ───────────

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, ChatCompletionException.RateLimited)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, ChatCompletionException.RateLimited)]
    [InlineData(HttpStatusCode.Unauthorized, ChatCompletionException.NotConfigured)]
    [InlineData(HttpStatusCode.Forbidden, ChatCompletionException.NotConfigured)]
    [InlineData(HttpStatusCode.InternalServerError, ChatCompletionException.Unavailable)]
    public async Task Vendor_failures_arrive_as_the_SAME_translation_keys_as_before(
        HttpStatusCode status, string expectedKey)
    {
        // ★ THIS IS WHAT LETS THE FRONT STAY UNTOUCHED. The retry button and its warning card key off
        // these constants; if a new provider invented its own codes, the resilience built last would
        // quietly stop applying to the provider that replaced the one it was built for.
        var (provider, _) = Build(new HttpResponseMessage(status)
        {
            Content = new StringContent("""{"error":{"message":"rate limited"}}"""),
        });

        var act = async () => await provider.CompleteJsonAsync(
            [new ChatMessage(ChatMessage.UserRole, "x")], CancellationToken.None);

        (await act.Should().ThrowAsync<ChatCompletionException>()).Which.ReasonKey.Should().Be(expectedKey);
    }

    [Fact]
    public async Task A_rejected_tool_call_keeps_its_own_reason_here_too()
    {
        // Recognised from the body's error code, exactly as on Groq — so the tool runner can shrug it
        // off and answer without live data rather than failing the turn.
        var (provider, _) = Build(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"code":"tool_use_failed","message":"failed to parse"}}"""),
        });

        var act = async () => await provider.SelectToolAsync(
            [new ChatMessage(ChatMessage.UserRole, "x")],
            [new AssistantToolSchema("get_transaction", "…", """{"type":"object"}""")],
            CancellationToken.None);

        (await act.Should().ThrowAsync<ChatCompletionException>())
            .Which.ReasonKey.Should().Be(ChatCompletionException.ToolCallRejected);
    }

    [Fact]
    public async Task The_vendors_own_error_TEXT_never_leaves_the_provider()
    {
        // A vendor's message can carry request ids, model names and slices of the prompt. The user gets
        // a key; the operator gets the detail in the log.
        var (provider, _) = Build(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{"error":{"message":"req_abc123 exceeded quota for prompt 'what happened with TERM-CC-10'"}}"""),
        });

        var act = async () => await provider.CompleteJsonAsync(
            [new ChatMessage(ChatMessage.UserRole, "x")], CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<ChatCompletionException>()).Which;

        thrown.ReasonKey.Should().StartWith("ASSISTANT.");
        thrown.ReasonKey.Should().NotContain("req_abc123");
        thrown.ReasonKey.Should().NotContain("TERM-CC-10");
    }

    // ── The interface did not change ──────────────────────────────────────────

    [Fact]
    public void Both_providers_satisfy_the_SAME_unchanged_interface()
    {
        // ★ The handler, the endpoints and the front never learn that a second vendor exists. That was
        // the whole promise of the vendor-neutral interface in 2a, and this is what keeps it honest:
        // adding a provider must not add a method.
        var methods = typeof(IChatCompletionProvider).GetMethods()
            .Where(m => !m.IsSpecialName).Select(m => m.Name).OrderBy(n => n).ToList();

        methods.Should().BeEquivalentTo(["CompleteJsonAsync", "SelectToolAsync", "StreamAsync"]);

        foreach (var implementation in new[] { typeof(GroqChatProvider), typeof(OpenRouterChatProvider) })
        {
            implementation.Should().BeAssignableTo<IChatCompletionProvider>();
        }
    }
}
