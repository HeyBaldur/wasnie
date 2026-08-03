using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Assistant.Common;
using Wasnie.Domain.Assistant;
using Wasnie.Infrastructure.Services.Assistant;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The assistant confined to Wasnie's own documentation.
///
/// ★ WHAT THESE CAN AND CANNOT PROVE. They assert that the documentation and the rules REACH the
/// model — which is deterministic and worth pinning. Whether the model then obeys is not: a test that
/// asserted "it refuses to give a recipe" would be asserting a property of somebody else's weights,
/// and would go red on a model upgrade that broke nothing. The obedience is judged on screen.
/// </summary>
public sealed class AssistantConfinementTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 15, 0, 0, TimeSpan.Zero);

    private static AssistantMessage UserTurn(string content, int sequence = 0) =>
        AssistantMessage.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            AssistantMessageRole.User, content, sequence, Now);

    private const string SampleDoc = "## 15. Clawback\nWasnie records a debt against the payee.";

    // ── 0. ★ The numeric rule, and the guide it defers to ─────────────────────

    [Fact]
    public void The_STRICT_NUMERIC_RULE_travels_with_every_confined_answer()
    {
        // ★ WHY THIS IS PINNED. The assistant once told a user to enter "1.00" for a rate, and the
        // report called it a hallucination. It was not — 1.00 IS 100% in this engine. But the same
        // reasoning pointed the other way ("5% so enter 5") configures five hundred per cent, and
        // nothing in rules 1-5 stops it: restating a number does not feel like invention to a model,
        // it feels like arithmetic. This rule is the only thing that does, so it must not be tidied
        // away in a future edit of the prompt.
        var system = AssistantPrompt.BuildSystemMessage("## 5. Rate tables\nOne rate applied.");

        system.Should().Contain(AssistantPrompt.NumericRule);
        system.Should().Contain("NEVER convert between conventions");
        system.Should().Contain("do not scale a value by 100");

        // ★ And the fallback that keeps it honest rather than merely strict: not knowing the format is
        // an answer, guessing it is not.
        system.Should().Contain("If the documentation does NOT state the input format");
        system.Should().Contain("Do not assume a convention");
    }

    [Fact]
    public void The_numeric_rule_states_no_convention_of_its_OWN()
    {
        // ★ THE PROMPT DEFERS, IT DOES NOT DECLARE. A second copy of "rates are decimals" living here
        // would be the copy that goes stale, and a prompt confidently contradicting the guide is worse
        // than one that points at it. The example teaches the BEHAVIOUR — repeat, do not recompute.
        AssistantPrompt.NumericRule.Should().Contain("the EXACT format the documentation gives");
        AssistantPrompt.NumericRule.Should().Contain("documentation states");

        // It must not assert the convention as a standalone fact.
        AssistantPrompt.NumericRule.Should().NotContain("Rates are entered as");
        AssistantPrompt.NumericRule.Should().NotContain("always a decimal");
    }

    [Fact]
    public void The_REAL_guide_states_the_rate_input_format_where_a_rate_question_lands()
    {
        // ★ MEASURED, NOT ASSUMED: the routing was tested against the live router, and the question
        // from the incident — "a plan that pays 100% with a cap of 200 EUR" — retrieved section 4 and
        // NOT section 5. A guardrail that lives only in the rate-tables section would not have reached
        // the model for the very question that caused this. So the guide states it in BOTH places, and
        // this is what keeps the second one from being "tidied up" as duplication.
        var knowledge = new FileAssistantKnowledgeBase(NullLogger<FileAssistantKnowledgeBase>.Instance);
        knowledge.IsAvailable.Should().BeTrue();

        var rateTables = knowledge.Sections.Single(s => s.Title.Contains("Rate tables", StringComparison.Ordinal));
        var planAndRules = knowledge.Sections.Single(s => s.Title.Contains("Plan and Rules", StringComparison.Ordinal));

        foreach (var section in new[] { rateTables, planAndRules })
        {
            section.Text.Should().Contain(
                "0.05",
                $"'{section.Title}' must state how a rate is typed — a question about rates can land here");
            section.Text.Should().Contain("1.00");
        }

        // The engine's own behaviour is cited, so the claim can be checked rather than believed.
        rateTables.Text.Should().Contain("Multiply(rateTable.FlatRate)");
        // And the one case that is not a percentage at all is called out.
        rateTables.Text.Should().Contain("money per unit");
    }

    // ── 1. ★ The documentation reaches the model ──────────────────────────────

    [Fact]
    public void The_system_message_CARRIES_the_documentation()
    {
        // ★ Revert the injection — pass the corpus and have Build ignore it — and this goes red.
        var prompt = AssistantPrompt.Build([UserTurn("does Wasnie support clawbacks?")], 20, SampleDoc, documentationAvailable: true);

        var system = prompt.Single(m => m.Role == ChatMessage.SystemRole);

        system.Content.Should().Contain(SampleDoc, "the documentation IS the assistant's knowledge");
        system.Content.Should().Contain(AssistantPrompt.DocumentationHeader);
        system.Content.Should().Contain(AssistantPrompt.DocumentationFooter);

        // The user's question is still the last thing the model reads.
        prompt[^1].Role.Should().Be(ChatMessage.UserRole);
        prompt[^1].Content.Should().Be("does Wasnie support clawbacks?");
    }

    [Fact]
    public void The_REAL_guide_reaches_the_model_including_the_clawback_section()
    {
        // The golden case from the report: "does Wasnie support clawbacks?" used to get a generic
        // industry answer. It can only be answered specifically if Wasnie's own clawback section is
        // actually in the context — so that is what is asserted.
        var knowledge = new FileAssistantKnowledgeBase(NullLogger<FileAssistantKnowledgeBase>.Instance);
        knowledge.IsAvailable.Should().BeTrue("the guide ships next to the binary — see Wasnie.Api.csproj");

        var system = AssistantPrompt.BuildSystemMessage(knowledge.Documentation);

        system.Should().Contain("15. Clawback");
        system.Should().Contain("records a **debt** against the payee");
    }

    // ── 2. ★ The confinement rules reach the model ────────────────────────────

    [Fact]
    public void The_system_message_CARRIES_the_three_refusals()
    {
        var system = AssistantPrompt.BuildSystemMessage(SampleDoc);

        // Only from the documentation…
        system.Should().Contain("ONLY SOURCE OF TRUTH");
        system.Should().Contain("Do not give a generic");
        // …say so when it is silent, and never invent…
        system.Should().Contain("SAY WHEN YOU DO NOT KNOW");
        system.Should().Contain("NEVER invent a feature");
        // …and decline what is not about Wasnie.
        system.Should().Contain("STAY ON WASNIE");

        // The 2a framing survives: it explains, it does not act — the same promise the on-screen
        // disclaimer makes, so the two cannot contradict each other.
        system.Should().Contain("YOU EXPLAIN, YOU DO NOT ACT");
    }

    [Fact]
    public void The_rules_are_restated_AFTER_the_documentation()
    {
        // Fifteen thousand tokens separate an instruction placed before the corpus from the user's
        // question. The restatement is what keeps the refusals from being buried by the material they
        // apply to — so its POSITION is the thing worth asserting, not merely its presence.
        var system = AssistantPrompt.BuildSystemMessage(SampleDoc);

        var footerAt = system.IndexOf(AssistantPrompt.DocumentationFooter, StringComparison.Ordinal);
        var reminderAt = system.IndexOf("Remember: answer only from the documentation", StringComparison.Ordinal);

        footerAt.Should().BeGreaterThan(0);
        reminderAt.Should().BeGreaterThan(footerAt, "the reminder must come after the corpus it guards");
    }

    // ── 3. The source loads ───────────────────────────────────────────────────

    [Fact]
    public void The_documentation_file_loads_and_is_substantial()
    {
        var knowledge = new FileAssistantKnowledgeBase(NullLogger<FileAssistantKnowledgeBase>.Instance);

        knowledge.IsAvailable.Should().BeTrue();
        knowledge.Documentation.Should().NotBeNullOrWhiteSpace();

        // A truncated or placeholder file would still "load". Real guidance runs to tens of thousands
        // of characters; anything far below that means the copy step silently produced a stub.
        knowledge.Documentation.Length.Should().BeGreaterThan(20_000);
        knowledge.Documentation.Should().Contain("Wasnie");
    }

    // ── 4. Degrading without the documentation ────────────────────────────────

    [Fact]
    public void Without_documentation_the_assistant_falls_back_and_ADMITS_it_is_unanchored()
    {
        // A missing file must not take the chat down. But the fallback must not keep speaking for the
        // product either — an assistant confidently describing Wasnie with no source is exactly the
        // failure this piece removes.
        // documentationAvailable: false is the "guide missing" silence, distinct from "the guide does
        // not cover this" — which is the no-source prompt, asserted in AssistantRoutingTests.
        var system = AssistantPrompt.BuildSystemMessage(string.Empty, documentationAvailable: false);

        system.Should().Be(AssistantPrompt.FallbackPrompt);
        system.Should().Contain("documentation is not available");
        system.Should().Contain("do not state specifics");
        system.Should().NotContain(AssistantPrompt.DocumentationHeader);

        // It still promises no actions — the disclaimer on screen stays true in every mode.
        system.Should().Contain("cannot perform actions");
    }

    [Fact]
    public void A_missing_file_yields_empty_documentation_rather_than_throwing()
    {
        var knowledge = new MissingKnowledgeBase();

        knowledge.IsAvailable.Should().BeFalse();
        AssistantPrompt.BuildSystemMessage(knowledge.Documentation, knowledge.IsAvailable)
            .Should().Be(AssistantPrompt.FallbackPrompt);
    }

    private sealed class MissingKnowledgeBase : IAssistantKnowledgeBase
    {
        public string Documentation => string.Empty;
        public bool IsAvailable => false;
        public IReadOnlyList<DocumentationSection> Sections => [];
        public string TableOfContents => string.Empty;
        public string TextFor(IEnumerable<string> sectionIds) => string.Empty;
    }

    // ── The 2a guarantees the new prompt must not disturb ──────────────────────

    [Fact]
    public void History_is_still_capped_and_stand_in_replies_are_still_dropped()
    {
        var conversationId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var history = new List<AssistantMessage>();
        for (var i = 0; i < 10; i++)
        {
            history.Add(AssistantMessage.Create(
                Guid.NewGuid(), conversationId, tenantId, AssistantMessageRole.User, $"turn {i}", i, Now));
        }
        history.Add(AssistantMessage.Create(
            Guid.NewGuid(), conversationId, tenantId, AssistantMessageRole.Assistant,
            AssistantMessage.NotConnectedPlaceholder, 10, Now));

        var prompt = AssistantPrompt.Build(history, maxHistory: 4, SampleDoc, documentationAvailable: true);

        // One system message plus the four newest real turns; the stand-in is not among them.
        prompt.Should().HaveCount(5);
        prompt[0].Role.Should().Be(ChatMessage.SystemRole);
        prompt.Skip(1).Select(m => m.Content).Should().Equal("turn 6", "turn 7", "turn 8", "turn 9");
        prompt.Should().NotContain(m => m.Content == AssistantMessage.NotConnectedPlaceholder);
    }
}
