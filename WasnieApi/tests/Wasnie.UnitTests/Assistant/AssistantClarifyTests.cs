using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Assistant.Tools;

namespace Wasnie.UnitTests.Assistant;

/// <summary>
/// KAN-58 — the clarify form's contract: what the model may offer, and how it shares the turn's row.
/// </summary>
public sealed class AssistantClarifyTests
{
    // Built by concatenation rather than interpolation: a raw interpolated literal cannot hold the
    // run of closing braces this JSON ends with without the brace count becoming its own puzzle.
    private static string ToolPayload(params string[] functions)
    {
        var options = string.Join(',', functions.Select(f => "{\"function\":\"" + f + "\"}"));
        return "{\"clarify\":{\"options\":[" + options + "],\"state\":\"open\"}}";
    }

    // ══ What may be offered ══════════════════════════════════════════════════════════════════

    [Fact]
    public void A_form_carries_the_functions_the_model_chose()
    {
        var form = AssistantClarify.Extract(ToolPayload("get_payee_balance", "get_payee_plans"));

        form.Should().NotBeNull();
        form!.Options.Select(o => o.Function)
            .Should().Equal("get_payee_balance", "get_payee_plans");
        form.State.Should().Be(ClarifyState.Open);
    }

    /// <summary>
    /// ★★ NO OPTIONS MEANS NO FORM, WHICH IS THE TICKET'S THIRD COMMENT AS CODE. "What is the weather?"
    /// must produce an honest sentence, not an empty panel and not a menu offering to look up a
    /// transaction. The absence is decided here rather than in the client, so every surface agrees.
    /// </summary>
    [Theory]
    [InlineData("""{"clarify":{"options":[],"state":"open"}}""")]
    [InlineData("""{"clarify":null}""")]
    [InlineData("""{"clarifyOffered":false}""")]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    public void Nothing_worth_offering_yields_no_form(string payload)
    {
        AssistantClarify.Extract(payload).Should().BeNull();
    }

    /// <summary>
    /// ★★ THE CAP IS ENFORCED, NOT REQUESTED. The prompt asks for at most three; this is what makes it
    /// true when the model ignores that. A form listing every function is the "menú tonto" the ticket's
    /// first comment rules out by name.
    /// </summary>
    [Fact]
    public void A_form_is_capped_at_three_options()
    {
        var form = AssistantClarify.Extract(ToolPayload(
            "get_transaction", "get_plan_rules", "get_payee_balance", "get_payee_plans", "simulate_plan_rules"));

        form!.Options.Should().HaveCount(ClarifyForm.MaxOptions);
    }

    [Fact]
    public void A_repeated_function_is_offered_once()
    {
        var form = AssistantClarify.Extract(ToolPayload("get_payee_balance", "get_payee_balance"));

        form!.Options.Should().ContainSingle();
    }

    // ══ Entity forms: which RECORD did you mean ══════════════════════════════════════════════

    private static ClarifyOption Person(string code, string name, string status) =>
        new("get_payee_balance", code, new ClarifyEntity(name, code, status));

    /// <summary>
    /// ★★ THE BLOCKER THAT MADE ENTITY FORMS IMPOSSIBLE, PINNED AS A TEST. The dedupe key used to be
    /// the FUNCTION alone, which is right while every option names a different one — and it silently
    /// destroyed this case: three options all running get_payee_balance for three different people are
    /// one function and three records. The form asking "which of these three?" showed one button.
    /// </summary>
    [Fact]
    public void Three_records_behind_ONE_function_are_three_options()
    {
        var form = AssistantClarify.EntityForm(
            [
                Person("EPO9009", "Camille Laurent", "Terminated"),
                Person("EMP409", "Camille Laurent", "Active"),
                Person("FR-301", "Camille Martin", "Active"),
            ],
            totalMatches: 3);

        form!.Options.Should().HaveCount(3);
        form.Options.Select(o => o.Argument).Should().Equal("EPO9009", "EMP409", "FR-301");
        form.IsEntityForm.Should().BeTrue();
    }

    /// <summary>★ And the record's own description rides along, so the screen can tell them apart.</summary>
    [Fact]
    public void Each_option_carries_the_record_it_stands_for()
    {
        var form = AssistantClarify.EntityForm(
            [Person("EPO9009", "Camille Laurent", "Terminated"), Person("FR-301", "Camille Martin", "Active")],
            totalMatches: 2);

        form!.Options[0].Entity.Should().Be(new ClarifyEntity("Camille Laurent", "EPO9009", "Terminated"));
    }

    /// <summary>
    /// ★★ THE SECOND BLOCKER: the cap. Three is right for a shortlist of FUNCTIONS and wrong for a list
    /// of PEOPLE — the candidates are not a shortlist somebody reasoned into, they are everyone who
    /// bears the name, and cutting them to three would hide real people behind a limit chosen to
    /// discipline a model.
    /// </summary>
    [Fact]
    public void An_entity_form_lists_more_than_a_function_form_may()
    {
        var many = Enumerable.Range(1, 12)
            .Select(i => Person($"EMP{i:000}", $"García {i}", "Active"))
            .ToList();

        var form = AssistantClarify.EntityForm(many, totalMatches: many.Count);

        form!.Options.Should().HaveCount(ClarifyForm.MaxEntityOptions);
        ClarifyForm.MaxEntityOptions.Should().BeGreaterThan(ClarifyForm.MaxOptions);
    }

    /// <summary>
    /// ★★ AND NOTHING IS LOST IN SILENCE. Showing eight of fifteen while implying it is all of them
    /// would be a confident partial answer — the exact shape of failure this ticket is about.
    /// </summary>
    [Fact]
    public void A_truncated_entity_form_still_reports_the_true_total()
    {
        var many = Enumerable.Range(1, 15)
            .Select(i => Person($"EMP{i:000}", $"García {i}", "Active"))
            .ToList();

        AssistantClarify.EntityForm(many, totalMatches: 15)!.TotalMatches.Should().Be(15);
    }

    /// <summary>★ When everything matched fits, there is no count to show and none is invented.</summary>
    [Fact]
    public void An_untruncated_entity_form_reports_no_total()
    {
        var form = AssistantClarify.EntityForm(
            [Person("EPO9009", "Camille Laurent", "Terminated"), Person("FR-301", "Camille Martin", "Active")],
            totalMatches: 2);

        form!.TotalMatches.Should().BeNull();
    }

    /// <summary>
    /// ★★ ONE CANDIDATE IS AN ANSWER, NOT A QUESTION. A form with a single button would ask the user to
    /// confirm something the system already resolved — and the resolver returns that person directly.
    /// </summary>
    [Fact]
    public void One_candidate_is_not_a_form()
    {
        AssistantClarify.EntityForm([Person("FR-301", "Camille Martin", "Active")], 1)
            .Should().BeNull();
    }

    /// <summary>★ An entity form survives the round trip through the payload with its kind intact.</summary>
    [Fact]
    public void An_entity_form_survives_serialisation()
    {
        var payload = AssistantClarify.ToPayload(AssistantClarify.EntityForm(
            [Person("EPO9009", "Camille Laurent", "Terminated"), Person("FR-301", "Camille Martin", "Active")],
            totalMatches: 2));

        var read = AssistantClarify.Extract(payload);

        read!.IsEntityForm.Should().BeTrue();
        read.Options.Should().HaveCount(2);
        read.Options[1].Entity!.Name.Should().Be("Camille Martin");
    }

    /// <summary>★ An unknown state is not trusted: it falls back to open rather than to nothing.</summary>
    [Fact]
    public void An_unrecognised_state_is_read_as_open()
    {
        var form = AssistantClarify.Extract(
            """{"clarify":{"options":[{"function":"get_payee_balance"}],"state":"whatever"}}""");

        form!.State.Should().Be(ClarifyState.Open);
    }

    // ══ Sharing the payload column ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ THE BUG THIS MERGE EXISTS TO PREVENT, AND IT WOULD HAVE BEEN SILENT. Both features serialise
    /// a COMPLETE object holding one key, so storing whichever was computed last drops the other — and
    /// losing the resolved entities breaks nothing visibly: the next turn simply asks for a name the
    /// thread had already resolved, which reads as the assistant being forgetful rather than as a bug.
    /// </summary>
    [Fact]
    public void The_clarify_form_and_the_resolved_entities_share_one_payload()
    {
        const string resolved =
            """{"resolvedEntities":{"payees":[{"id":"3f2a77bc-1c4e-4a0d-8f11-9a0b7c5d2e64","name":"Ana"}]}}""";
        var clarify = AssistantClarify.ToPayload(
            new ClarifyForm([new ClarifyOption("get_payee_balance", "Ana")], ClarifyState.Open));

        var merged = AssistantClarify.Merge(resolved, clarify);

        merged.Should().Contain("resolvedEntities", "the entity context must survive");
        merged.Should().Contain("clarify", "and so must the form");
        AssistantClarify.Extract(merged)!.Options.Should().ContainSingle();
    }

    [Fact]
    public void Merging_nothing_leaves_the_column_null()
    {
        AssistantClarify.Merge(null, null).Should().BeNull();
        AssistantClarify.Merge().Should().BeNull();
    }

    /// <summary>★ One unreadable fragment must not cost the other its place in the row.</summary>
    [Fact]
    public void A_broken_fragment_does_not_take_the_good_one_with_it()
    {
        var merged = AssistantClarify.Merge("{ this is not json", """{"resolvedEntities":{}}""");

        merged.Should().Contain("resolvedEntities");
    }

    // ══ Moving state ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ THE STATE MOVES AND EVERYTHING ELSE IN THE COLUMN STAYS. This is the refresh behaviour the
    /// ticket's third comment asks for: a dismissed form is still dismissed after a reload, and the
    /// resolved entities the same turn carried are not collateral damage of closing a panel.
    /// </summary>
    [Fact]
    public void Resolving_a_form_keeps_the_rest_of_the_payload()
    {
        var payload = AssistantClarify.Merge(
            """{"resolvedEntities":{"payees":[{"id":"3f2a77bc-1c4e-4a0d-8f11-9a0b7c5d2e64","name":"Ana"}]}}""",
            AssistantClarify.ToPayload(
                new ClarifyForm([new ClarifyOption("get_payee_balance", null)], ClarifyState.Open)));

        var dismissed = AssistantClarify.WithState(payload, ClarifyState.Dismissed);

        AssistantClarify.Extract(dismissed)!.State.Should().Be(ClarifyState.Dismissed);
        dismissed.Should().Contain("resolvedEntities");
        dismissed.Should().Contain("Ana");
    }

    [Fact]
    public void Resolving_a_payload_that_has_no_form_changes_nothing()
    {
        const string payload = """{"resolvedEntities":{}}""";

        AssistantClarify.WithState(payload, ClarifyState.Dismissed).Should().Be(payload);
    }

    // ══ The tool ═════════════════════════════════════════════════════════════════════════════

    private static AskUserToChooseTool Tool() => new(NullLogger<AskUserToChooseTool>.Instance);

    private static string Run(string argumentsJson) =>
        Tool().RunAsync(argumentsJson, CancellationToken.None).GetAwaiter().GetResult();

    [Fact]
    public void The_tool_offers_the_functions_the_model_named()
    {
        var payload = Run("""{"options":[{"function":"get_payee_balance","argument":"Aleksandra"}]}""");

        var form = AssistantClarify.Extract(payload);
        form!.Options.Should().ContainSingle();
        form.Options[0].Function.Should().Be("get_payee_balance");
        form.Options[0].Argument.Should().Be("Aleksandra");
    }

    /// <summary>
    /// ★★ A FUNCTION NAME THE MODEL INVENTED IS DROPPED. This is the same class of failure as the
    /// invented payout id KAN-58 is named for, and it is invisible from the screen: an option built on
    /// a function that does not exist would render a button that runs nothing.
    /// </summary>
    [Fact]
    public void An_invented_function_name_is_not_offered()
    {
        var payload = Run(
            """{"options":[{"function":"get_payouts"},{"function":"get_payee_balance"}]}""");

        var form = AssistantClarify.Extract(payload);
        form!.Options.Select(o => o.Function).Should().Equal("get_payee_balance");
    }

    /// <summary>★ And when NOTHING survives, the tool says so instead of returning an empty panel.</summary>
    [Theory]
    [InlineData("""{"options":[{"function":"get_payouts"}]}""")]
    [InlineData("""{"options":[]}""")]
    [InlineData("""{"nonsense":true}""")]
    [InlineData("not json")]
    [InlineData("")]
    public void No_offerable_function_means_no_form_at_all(string arguments)
    {
        var payload = Run(arguments);

        payload.Should().Contain("\"clarifyOffered\":false");
        AssistantClarify.Extract(payload).Should().BeNull();
    }

    /// <summary>
    /// ★ THE INSTRUCTION STOPS THE MODEL PRINTING THE MENU TWICE. Without it the answer reads "you can
    /// check her balance or her assignments — which would you like?" directly above a panel offering
    /// exactly those two buttons.
    /// </summary>
    [Fact]
    public void The_tool_tells_the_model_the_form_is_already_on_screen()
    {
        var payload = Run("""{"options":[{"function":"get_payee_balance"}]}""");

        payload.Should().Contain("ALREADY been shown");
        payload.Should().Contain("Do NOT list the options again");
    }

    [Fact]
    public void The_tool_is_capped_like_the_contract_it_feeds()
    {
        var payload = Run(
            """
            {"options":[{"function":"get_transaction"},{"function":"get_plan_rules"},
                        {"function":"get_payee_balance"},{"function":"get_payee_plans"}]}
            """);

        AssistantClarify.Extract(payload)!.Options.Should().HaveCount(ClarifyForm.MaxOptions);
    }
}
