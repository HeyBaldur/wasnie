using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Assistant.Common;
using Wasnie.Domain.Assistant;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// THE ID REACHES THE COMPONENT THAT CAN USE IT.
///
/// ★ THE REPRODUCED BUG. Turn 1: "what is Ana García's balance?" — answered correctly. Turn 2: "and what
/// plans does she have assigned?" — "I cannot find Ana García". The payee had just been resolved.
///
/// ★ AND THE FIX THAT WAS SHIPPED FOR IT DID NOTHING, which is the more useful half of this suite. The
/// previous work item put a payeeId in every payload and wrote an iron rule ordering the model to reuse
/// it. The rule went into <c>AssistantPrompt.DataRules</c> — read by the model that composes the ANSWER,
/// which never calls a tool and has no argument to put an id in. The component that DOES write arguments
/// is the dispatcher, and it was shown message text only. The instruction was correct, delivered to a
/// reader who could not act on it, and it looked enough like a fix to hide the real gap.
///
/// So these tests assert the two halves that were missing: the context REACHES the dispatcher, and the
/// rule that governs it LIVES in the dispatcher's own instructions.
/// </summary>
public sealed class AssistantEntityContinuityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Conversation = Guid.NewGuid();
    private static readonly Guid Tenant = Guid.NewGuid();

    /// <summary>Records exactly what the dispatcher was shown, and answers with a fixed choice.</summary>
    private sealed class RecordingProvider : IChatCompletionProvider
    {
        public IReadOnlyList<ChatMessage>? SelectionMessages { get; private set; }

        public AssistantToolRequest? ToolChoice { get; set; }

        public bool IsConfigured => true;

        public Task<AssistantToolRequest?> SelectToolAsync(
            IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<AssistantToolSchema> tools,
            CancellationToken cancellationToken)
        {
            SelectionMessages = messages;
            return Task.FromResult(ToolChoice);
        }

        public IAsyncEnumerable<string> StreamAsync(
            IReadOnlyList<ChatMessage> m, CancellationToken c) => throw new NotSupportedException();

        public Task<string> CompleteJsonAsync(
            IReadOnlyList<ChatMessage> m, CancellationToken c) => throw new NotSupportedException();
    }

    private sealed class EchoTool(string name) : IAssistantTool
    {
        public AssistantToolSchema Schema { get; } =
            new(name, "d", """{"type":"object","properties":{}}""");

        public Task<string> RunAsync(string argumentsJson, CancellationToken cancellationToken) =>
            Task.FromResult(argumentsJson);
    }

    private static AssistantMessage Turn(
        AssistantMessageRole role, string content, int sequence, string? payload = null) =>
        AssistantMessage.Create(
            Guid.NewGuid(), Conversation, Tenant, role, content, sequence, Now, payload);

    private static string BalanceOf(Guid payeeId, string name) =>
        ResolvedEntityContext.PayloadFor(
            $$"""
            {"found":true,"payeeId":"{{payeeId}}","payeeName":"{{name}}","matchedBy":"ExactName",
             "balances":[{"currency":"EUR","earnedCommissions":78298.24,"outstandingDebt":0}]}
            """)!;

    private static AssistantToolRunner Runner(RecordingProvider provider) =>
        new(provider,
            [new EchoTool("get_payee_balance"), new EchoTool("get_payee_plans")],
            NullLogger<AssistantToolRunner>.Instance);

    private static string ContextShownTo(RecordingProvider provider) =>
        string.Join("\n", provider.SelectionMessages!.Select(m => m.Content));

    // ══ THE BUG, AT THE SEAM WHERE IT BROKE ═══════════════════════════════════

    [Fact]
    public async Task THE_PAYEE_ID_FROM_THE_BALANCE_TURN_REACHES_THE_DISPATCHER_ON_THE_NEXT_QUESTION()
    {
        // ★ THE REPRODUCED CASE. Turn 1 resolved Ana through the BALANCE tool; turn 2 asks about her
        // PLANS and names nobody. Before the channel existed there was no path by which Ana's id could
        // be in this call — the payload was discarded and rule 18 kept the id out of the reply.
        var ana = Guid.NewGuid();
        var provider = new RecordingProvider();

        await Runner(provider).RunAsync(
            "¿y qué planes tiene asignados?",
            [
                Turn(AssistantMessageRole.User, "¿cuál es el balance de Ana García?", 0),
                Turn(AssistantMessageRole.Assistant, "Ana García tiene 78.298,24 EUR pendientes…", 1,
                    BalanceOf(ana, "Ana García")),
            ],
            CancellationToken.None);

        var shown = ContextShownTo(provider);

        shown.Should().Contain(ana.ToString(),
            "★ the id resolved by ONE tool must be available to the dispatcher for ANOTHER");
        shown.Should().Contain("Ana García");
    }

    [Fact]
    public async Task The_context_survives_further_back_than_the_truncated_transcript()
    {
        // ★ THE TWO LIMITS MUST NOT REACH THE IDS. The transcript is capped at four messages and each is
        // cut to 600 characters — right for a classifier deciding about the current question, wrong for
        // an identifier that cannot be "mostly" correct. A payee resolved six turns ago is exactly the
        // one a comparison reaches back for, so the context is built from the FULL history.
        var ana = Guid.NewGuid();
        var provider = new RecordingProvider();

        var history = new List<AssistantMessage>
        {
            Turn(AssistantMessageRole.User, "¿balance de Ana García?", 0),
            Turn(AssistantMessageRole.Assistant, "Ana García: 78.298,24 EUR…", 1, BalanceOf(ana, "Ana García")),
        };

        for (var i = 2; i < 12; i++)
        {
            history.Add(Turn(
                i % 2 == 0 ? AssistantMessageRole.User : AssistantMessageRole.Assistant,
                $"una vuelta más de conversación sobre otra cosa, número {i}", i));
        }

        await Runner(provider).RunAsync("¿y sus planes?", history, CancellationToken.None);

        ContextShownTo(provider).Should().Contain(ana.ToString(),
            "ten turns later the transcript no longer mentions Ana, but her id must still be reachable");
    }

    [Fact]
    public async Task Two_payees_from_two_turns_are_BOTH_offered_for_a_comparison()
    {
        var ana = Guid.NewGuid();
        var bruno = Guid.NewGuid();
        var provider = new RecordingProvider();

        await Runner(provider).RunAsync(
            "compará las asignaciones de los dos",
            [
                Turn(AssistantMessageRole.User, "¿balance de Ana García?", 0),
                Turn(AssistantMessageRole.Assistant, "Ana García…", 1, BalanceOf(ana, "Ana García")),
                Turn(AssistantMessageRole.User, "¿y el de Bruno Díaz?", 2),
                Turn(AssistantMessageRole.Assistant, "Bruno Díaz…", 3, BalanceOf(bruno, "Bruno Díaz")),
            ],
            CancellationToken.None);

        var shown = ContextShownTo(provider);

        shown.Should().Contain(ana.ToString()).And.Contain(bruno.ToString(),
            "a 'last entity' anchor would have kept only Bruno, and the comparison would answer about one");
    }

    [Fact]
    public async Task A_first_question_carries_no_context_block_at_all()
    {
        var provider = new RecordingProvider();

        await Runner(provider).RunAsync(
            "¿cuál es el balance de Ana García?", [], CancellationToken.None);

        ContextShownTo(provider).Should().NotContain(ResolvedEntityContext.Header,
            "an empty section announcing there is no context teaches the model to expect one");
    }

    [Fact]
    public async Task The_context_is_reference_data_placed_before_the_conversation()
    {
        // Put after the turns it would be the most recent thing "said", and the classifier would start
        // deciding about it instead of about the question.
        var provider = new RecordingProvider();

        await Runner(provider).RunAsync(
            "¿y sus planes?",
            [
                Turn(AssistantMessageRole.User, "¿balance de Ana García?", 0),
                Turn(AssistantMessageRole.Assistant, "Ana García…", 1,
                    BalanceOf(Guid.NewGuid(), "Ana García")),
            ],
            CancellationToken.None);

        var messages = provider.SelectionMessages!;
        var contextAt = messages.ToList().FindIndex(m => m.Content.Contains(ResolvedEntityContext.Header));

        contextAt.Should().BeGreaterThan(-1);
        messages[contextAt].Role.Should().Be(ChatMessage.SystemRole);
        messages[^1].Content.Should().Be("¿y sus planes?", "the question stays last");
        contextAt.Should().BeLessThan(messages.Count - 1);
    }

    // ══ THE RULE LIVES WHERE IT CAN ACT ═══════════════════════════════════════

    [Fact]
    public void THE_IRON_RULE_IS_IN_THE_DISPATCHERS_PROMPT_NOT_THE_ANSWERING_MODELS()
    {
        // ★ THE STRUCTURAL HALF OF THE FIX. Rules 10b/10c ordered a model with no tool call to pass an
        // id; this asserts the instruction now sits with the component that fills in arguments, and that
        // the answering prompt no longer pretends to.
        AssistantToolRunner.SelectionInstructions.Should().Contain(
            AssistantToolRunner.IdentifierRules);

        AssistantToolRunner.IdentifierRules.Should().Contain("payeeId").And.Contain("planId");

        AssistantPrompt.DataRules.Should().NotContain("pass THAT id to the tool",
            "the answering model cannot pass anything to a tool");
    }

    [Fact]
    public void The_rule_is_written_about_the_ENTITY_so_it_crosses_tools()
    {
        // The reproduced failure was a jump BETWEEN tools. A rule phrased per-tool would not have
        // reached it, however strictly it were worded.
        AssistantToolRunner.IdentifierRules.Should().Contain("THE ID CROSSES TOOLS");
        AssistantToolRunner.IdentifierRules.Should().Contain("SEVERAL ENTITIES",
            "forbidding a second lookup must not quietly forbid comparisons");
    }

    [Fact]
    public void The_answering_prompt_still_forbids_printing_an_id()
    {
        // The half of 10b that WAS this model's business, and the one leak the channel could introduce:
        // ids now travel on every lookup.
        AssistantPrompt.DataRules.Should().Contain("NEVER put an id in your answer");
    }

    [Fact]
    public void The_dispatcher_is_told_a_person_is_not_a_plan()
    {
        // The routing half of the bug: with no assignments tool, "what plans does Ana have" went to
        // get_plan_rules with "Ana García" as the plan name.
        AssistantToolRunner.SelectionInstructions.Should()
            .Contain("A QUESTION ABOUT A PERSON IS NOT A QUESTION ABOUT A PLAN");
    }
}
