using FluentAssertions;
using Wasnie.Application.Assistant.Common;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// Where an instruction about CHOOSING a tool has to live.
///
/// ★★ THIS IS A REGRESSION GUARD FOR A MISTAKE MADE TWICE. Rules 10b/10c were written into
/// <c>AssistantPrompt.DataRules</c> and had no effect on any lookup; they were moved to the
/// dispatcher's prompt. Then rules 10e-10h were written into the same block, for the same reason, with
/// the same result — `simulate_plan_rules` was never once invoked, established from the logs in
/// docs/ASSISTANT_SIMULATION_TOOL_NOT_CALLED.md.
///
/// ★ THE BLOCK IS UNREACHABLE AT DECISION TIME, TWICE OVER. <c>DataRules</c> is only assembled when a
/// tool ALREADY RAN (AssistantPrompt.BuildSystemMessage), and even then it goes to the model that
/// composes the ANSWER — never to <c>SelectAsync</c>, which is a separate call with its own prompt.
///
/// A comment saying so already existed and did not stop it happening again. A test does not rely on
/// anybody reading a comment.
/// </summary>
public sealed class ToolSelectionInstructionsTests
{
    // ══ ★ The instruction is where it can act ═════════════════════════════

    [Fact]
    public void THE_SIMULATION_INSTRUCTION_LIVES_IN_THE_DISPATCHERS_PROMPT()
    {
        var instructions = AssistantToolRunner.SelectionInstructions;

        instructions.Should().Contain("SIMULATION tool",
            "the only prompt that can change which tool is called is this one");
        instructions.Should().Contain("AMOUNT or a QUANTITY");
    }

    [Fact]
    public void It_names_the_phrasings_a_user_actually_uses()
    {
        // ★ THE MODEL CHOOSES ON VOCABULARY OVERLAP. The first version described "a hypothetical
        // sale"; the user said "I have a transaction of 7,850 with 5 units". The words have to be the
        // user's.
        var instructions = AssistantToolRunner.SelectionInstructions;

        foreach (var phrase in new[]
        {
            "how much does each rule generate",
            "what would this pay",
            "if I sell X",
            "simulate",
        })
        {
            instructions.Should().Contain(phrase);
        }
    }

    [Fact]
    public void IT_DISAMBIGUATES_AGAINST_THE_CONFIGURATION_LOOKUP()
    {
        // ★ THE COLLISION THAT ACTUALLY HAPPENED. "Why did my commission come out that way" sits in
        // the plan-rules case and swallowed the question. Saying which is which is the fix — the same
        // treatment the payee/plan pair already got.
        var instructions = AssistantToolRunner.SelectionInstructions;

        instructions.Should().Contain("CONFIGURATION");
        instructions.Should().Contain("CALCULATION");
        instructions.Should().Contain("ONE tool call per turn",
            "the ceiling is why the tie is broken toward simulation");
    }

    // ══ ★★ And it is NOT in the block that cannot act ═════════════════════

    [Fact]
    public void AN_INSTRUCTION_TO_CALL_THE_SIMULATOR_MUST_NOT_LIVE_IN_DATA_RULES()
    {
        // ★★ THE EXACT MISTAKE, BLOCKED. DataRules only ships once a lookup has already run, and only
        // to the answering model. Naming a tool there is an instruction that arrives after the moment
        // it was for, addressed to a reader who cannot act on it.
        var dataRules = AssistantPrompt.DataRules;

        dataRules.Should().NotContain("simulate_plan_rules",
            "naming a tool to call belongs in AssistantToolRunner.SelectionInstructions");
        dataRules.Should().NotContain("get_plan_rules");
        dataRules.Should().NotContain("get_transaction");
    }

    [Fact]
    public void WHAT_DATA_RULES_DOES_KEEP_IS_HOW_TO_REPORT_A_RESULT()
    {
        // ★ 10e-10h ARE NOT DELETED, AND THAT IS DELIBERATE. They are inert for the choice and they
        // govern the reporting in the turn where the simulator does run: the engine's order, the
        // provenance of a supplied figure, and not totalling rules into something that looks like a
        // payout.
        var dataRules = AssistantPrompt.DataRules;

        dataRules.Should().Contain("Supplied OR Defaulted");
        dataRules.Should().Contain("in the order returned");
        dataRules.Should().Contain("a sum of rules is not a payout");
    }

    [Fact]
    public void The_warning_that_explains_all_of_this_sits_next_to_the_block_it_is_about()
    {
        // ★ A COMMENT DID NOT STOP IT HAPPENING TWICE, so this pins that the warning is at least where
        // somebody editing the rules will read it — beside DataRules, not buried in the runner.
        var source = System.IO.File.ReadAllText(SourcePath());

        source.Should().Contain("A RULE ABOUT WHICH TOOL TO CALL DOES NOT BELONG HERE");
    }

    /// <summary>The prompt file, found from the test binary rather than hard-coded to one machine.</summary>
    private static string SourcePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the repository root has a src/ directory");
        return Path.Combine(
            dir!.FullName, "src", "Wasnie.Application", "Assistant", "Common", "AssistantPrompt.cs");
    }
}
