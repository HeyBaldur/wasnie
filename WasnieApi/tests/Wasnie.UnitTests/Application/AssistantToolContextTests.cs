using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Assistant.Common;
using Wasnie.Domain.Assistant;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// THE DISPATCHER CAN SEE THE CONVERSATION.
///
/// ★ THE INCIDENT. Turn 1: "explain the plan Q3 2026 — Plan Comercial EMEA (Test Integral)" — explained
/// perfectly, all three rules. Turn 2: "I have a transaction for 149.000 for 200 laptops, how many
/// credits does it generate?" — "no plan with that name was found, or you do not have access to it".
/// Two messages after describing it.
///
/// ★ AND IT WAS NOT A NAME PROBLEM, which is what made it worth instrumenting rather than guessing. The
/// logged tool call for turn 2 was literally <c>{"planName": null}</c>. The dispatcher received the
/// current message and NOTHING ELSE, and the current message names no plan — because people do not
/// repeat a title they used one sentence ago. No amount of name normalisation reaches a null; what was
/// missing was the conversation.
/// </summary>
public sealed class AssistantToolContextTests
{
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

    private sealed class EchoTool : IAssistantTool
    {
        public AssistantToolSchema Schema { get; } =
            new("get_plan_rules", "d", """{"type":"object","properties":{}}""");

        public Task<string> RunAsync(string argumentsJson, CancellationToken cancellationToken) =>
            Task.FromResult(argumentsJson);
    }

    private const string PlanName = "Q3 2026 — Plan Comercial EMEA (Test Integral)";
    private const string FollowUp = "tengo una transacción por 149000 por 200 laptops, ¿cuántos créditos genera?";

    private static AssistantMessage Message(AssistantMessageRole role, string content, int sequence) =>
        AssistantMessage.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), role, content, sequence,
            new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero));

    private static IReadOnlyList<AssistantMessage> TheIncident() =>
    [
        Message(AssistantMessageRole.User, $"explícame el plan {PlanName}", 0),
        Message(AssistantMessageRole.Assistant,
            $"El plan **{PlanName}** está activo, moneda EUR, y tiene tres reglas…", 1),
    ];

    private static AssistantToolRunner Runner(RecordingProvider provider) =>
        new(provider, [new EchoTool()], NullLogger<AssistantToolRunner>.Instance);

    [Fact]
    public async Task The_dispatcher_is_shown_the_EARLIER_turns_so_a_follow_up_has_a_referent()
    {
        // ★ THE FIX, ASSERTED WHERE IT LIVES. The plan's name is nowhere in the follow-up; it is in the
        // turn before. If the dispatcher cannot see that turn, no prompt and no normalisation can save
        // the lookup.
        var provider = new RecordingProvider();

        await Runner(provider).RunAsync(FollowUp, TheIncident(), CancellationToken.None);

        var shown = provider.SelectionMessages.Should().NotBeNull().And.Subject.ToList();

        shown.Should().Contain(m => m.Content.Contains(PlanName),
            "the name the follow-up refers to must be visible to the dispatcher");
        shown[0].Role.Should().Be(ChatMessage.SystemRole);
        shown[^1].Content.Should().Be(FollowUp, "the question comes LAST so the decision is about IT");
    }

    [Fact]
    public async Task The_instructions_TELL_it_to_resolve_references_from_that_context()
    {
        // Showing the context without saying what it is for invites the classifier to answer the old
        // question instead of the new one.
        var provider = new RecordingProvider();

        await Runner(provider).RunAsync(FollowUp, TheIncident(), CancellationToken.None);

        provider.SelectionMessages![0].Content.Should()
            .Contain("CONTEXT").And.Contain("LAST user message");
    }

    [Fact]
    public async Task The_current_question_is_not_shown_TWICE_when_the_caller_already_appended_it()
    {
        // ★ THE TWO ANSWER PATHS DIFFER ON THIS. PostMessageHandler appends the user's message to the
        // history before the lookup; the streaming handler does not. A dispatcher reading the same
        // sentence twice is being told it matters twice, and the two paths must not decide differently.
        var provider = new RecordingProvider();
        var history = TheIncident().Append(Message(AssistantMessageRole.User, FollowUp, 2)).ToList();

        await Runner(provider).RunAsync(FollowUp, history, CancellationToken.None);

        provider.SelectionMessages!.Count(m => m.Content == FollowUp).Should().Be(1);
    }

    [Fact]
    public async Task Only_the_RECENT_turns_travel_and_each_one_is_truncated()
    {
        // The classifier is on the critical path of every turn. A whole thread of three-thousand-
        // character answers would make it slow, expensive, and prone to deciding about the wrong turn.
        var provider = new RecordingProvider();
        var long_ = new string('x', 5_000);
        var history = Enumerable.Range(0, 20)
            .Select(i => Message(
                i % 2 == 0 ? AssistantMessageRole.User : AssistantMessageRole.Assistant, long_ + i, i))
            .ToList();

        await Runner(provider).RunAsync(FollowUp, history, CancellationToken.None);

        var shown = provider.SelectionMessages!;
        shown.Count.Should().BeLessThan(8, "instructions + a few turns + the question");
        shown.Where(m => m.Content.StartsWith('x'))
            .Should().OnlyContain(m => m.Content.Length <= 600);
    }

    [Fact]
    public async Task A_FIRST_message_still_works_with_no_history_at_all()
    {
        var provider = new RecordingProvider();

        await Runner(provider).RunAsync("explícame mi plan", [], CancellationToken.None);

        var shown = provider.SelectionMessages!;
        shown.Should().HaveCount(2, "the instructions and the question, and nothing invented between");
        shown[^1].Content.Should().Be("explícame mi plan");
    }

    [Fact]
    public async Task Context_does_not_change_WHAT_the_tool_receives()
    {
        // ★ THE CONTEXT IS FOR THE DISPATCHER, NOT FOR THE TOOL. The tool still gets exactly the
        // arguments the model generated — nothing here rewrites them behind its back, which is what
        // would make the lookup untraceable to what was asked.
        var provider = new RecordingProvider
        {
            ToolChoice = new AssistantToolRequest("get_plan_rules", $$"""{"planName":"{{PlanName}}"}"""),
        };

        var outcome = await Runner(provider).RunAsync(FollowUp, TheIncident(), CancellationToken.None);

        outcome.Data.Should().Be($$"""{"planName":"{{PlanName}}"}""");
        outcome.DidFail.Should().BeFalse();
    }
}
