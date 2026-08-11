using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Common.Options;
using Wasnie.Infrastructure.Integrations.OpenRouter;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// TWO MODELS, BECAUSE THE TWO JOBS FAIL DIFFERENTLY.
///
/// ★ WHY THE SPLIT EXISTS. The router and the tool dispatcher CLASSIFY — pick a section, pick a tool —
/// and a small fast model does that well for a fraction of a cent. The generation call WRITES THE
/// ANSWER, and when a small model breaks there it does not merely answer worse: gpt-oss-20b fell into a
/// repetition loop mid-explanation, on screen, in a product about people's pay. So robustness is bought
/// where it is read, and thrift kept where it is not.
///
/// ★ AND THE WIRE IS WHAT IS TESTED. Asserting the options object holds two strings would prove
/// nothing — the property worth having is that the STREAM request carries the generation model while
/// the router and tool requests carry the cheap one, and only the HTTP body shows that.
/// </summary>
public sealed class AssistantModelSplitTests
{
    private const string RouterModel = "test/router-small";
    private const string GenerationModel = "test/generation-large";

    /// <summary>Captures the outgoing request body without letting it reach a vendor.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Bodies.Add(body);

            // The streaming call wants SSE; the router and the tool dispatcher want a JSON completion.
            // Which one it is, is read off the request the provider actually built.
            var streaming = body.Contains("\"stream\":true", StringComparison.Ordinal);

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(streaming
                    ? "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\ndata: [DONE]\n\n"
                    : """
                      {"choices":[{"message":{"content":"{}","tool_calls":[
                        {"function":{"name":"get_plan_rules","arguments":"{}"}}]}}]}
                      """),
            };
        }
    }

    private sealed class OneClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (OpenRouterChatProvider Provider, CapturingHandler Http) Build()
    {
        var http = new CapturingHandler();

        var provider = new OpenRouterChatProvider(
            new OneClientFactory(http),
            Options.Create(new OpenRouterOptions
            {
                ApiKey = "test-key",
                Model = RouterModel,
                GenerationModel = GenerationModel,
            }),
            NullLogger<OpenRouterChatProvider>.Instance);

        return (provider, http);
    }

    private static readonly IReadOnlyList<ChatMessage> Messages =
        [new(ChatMessage.SystemRole, "rules"), new(ChatMessage.UserRole, "question")];

    [Fact]
    public async Task The_ANSWER_the_user_reads_is_generated_by_the_generation_model()
    {
        var (provider, http) = Build();

        await foreach (var _ in provider.StreamAsync(Messages, CancellationToken.None)) { }

        http.Bodies.Should().ContainSingle();
        http.Bodies[0].Should().Contain(GenerationModel)
            .And.NotContain(RouterModel, "the streamed answer must not fall back to the cheap model");
    }

    [Fact]
    public async Task The_ROUTER_stays_on_the_small_model()
    {
        var (provider, http) = Build();

        await provider.CompleteJsonAsync(Messages, CancellationToken.None);

        http.Bodies[0].Should().Contain(RouterModel)
            .And.NotContain(GenerationModel, "classification does not need the expensive model");
    }

    [Fact]
    public async Task The_TOOL_DISPATCHER_stays_on_the_small_model()
    {
        var (provider, http) = Build();

        await provider.SelectToolAsync(
            Messages,
            [new AssistantToolSchema("get_plan_rules", "d", """{"type":"object","properties":{}}""")],
            CancellationToken.None);

        http.Bodies[0].Should().Contain(RouterModel).And.NotContain(GenerationModel);
    }

    [Fact]
    public async Task An_appsettings_that_predates_the_split_keeps_working_unchanged()
    {
        // ★ NO SILENT BREAKAGE ON DEPLOY. A configuration written before GenerationModel existed has an
        // empty value for it; the generation call must then use the model it always used, not send an
        // empty model id and fail every request.
        var http = new CapturingHandler();
        var provider = new OpenRouterChatProvider(
            new OneClientFactory(http),
            Options.Create(new OpenRouterOptions
            {
                ApiKey = "test-key",
                Model = RouterModel,
                GenerationModel = "",
            }),
            NullLogger<OpenRouterChatProvider>.Instance);

        await foreach (var _ in provider.StreamAsync(Messages, CancellationToken.None)) { }

        http.Bodies[0].Should().Contain(RouterModel);
    }
}
