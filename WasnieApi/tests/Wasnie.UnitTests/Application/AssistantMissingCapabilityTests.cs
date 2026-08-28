using FluentAssertions;
using Wasnie.Application.Assistant.Common;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// Scenario 2D: the assistant admits a capability does not exist instead of reporting a false "not found".
///
/// ★ WHAT HAPPENED, AND WHY A PROMPT TEST IS THE RIGHT TEST HERE. On 2026-08-18 a user asked which payees
/// were on a plan they were looking at on screen. No lookup runs plan → payees, so the dispatcher reached
/// for the PAYEE tools and handed them the plan's name, and then its UUID. Both answered truthfully that no
/// such person exists. The answering model, given a genuine not-found, applied scenario 2C — "ask for the
/// exact name or id" — and asked for the identifier the user had already supplied, four times over. Nothing
/// in the stack was lying and the user was told, repeatedly, that a plan they could see did not exist.
///
/// ★ WHAT THESE CAN AND CANNOT PROVE, same as the confinement tests: they assert that the rules REACH the
/// model and that they say the right thing, which is deterministic. Whether the model then obeys is a
/// property of somebody else's weights and is judged on screen.
/// </summary>
public sealed class AssistantMissingCapabilityTests
{
    // ── The category exists and is reachable ─────────────────────────────────

    [Fact]
    public void The_ignorance_rules_carry_a_fourth_category()
    {
        AssistantPrompt.IgnoranceRules.Should().Contain("2D.");
    }

    // The taxonomy states its own size, and the model is told to pick from it. A count left at three
    // would keep pushing "I cannot look this up" into the nearest of the first three — which is 2C.
    [Fact]
    public void The_taxonomy_says_there_are_four_kinds_of_not_knowing()
    {
        AssistantPrompt.IgnoranceRules.Should().Contain("exactly four");
        AssistantPrompt.IgnoranceRules.Should().NotContain("exactly three");
    }

    // The "never send them to an administrator" rule redirects the model into the taxonomy by name. Left
    // listing only three, it would route a missing capability into one of them.
    [Fact]
    public void The_administrator_rule_offers_the_fourth_category_too()
    {
        AssistantPrompt.IgnoranceRules.Should().Contain("2A, 2B, 2C or 2D");
    }

    // ── What 2D must and must not say ────────────────────────────────────────

    // ★ The heart of it. 2C's move is to ask for the exact name or id; in 2D that is a circle, because
    // there is no lookup to give the identifier to. This is the sentence that broke the loop.
    [Fact]
    public void The_fourth_category_forbids_asking_for_an_identifier()
    {
        var rules = AssistantPrompt.IgnoranceRules;
        var section = rules[rules.IndexOf("2D.", StringComparison.Ordinal)..];

        section.Should().Contain("DO NOT ASK FOR THE EXACT NAME OR THE ID");
    }

    // ★ "The capability is not available" and "I could not find it" are different claims, and only one of
    // them is true. Saying the second about a record the user is looking at is the whole bug.
    [Fact]
    public void The_fourth_category_separates_a_missing_capability_from_a_missing_record()
    {
        var rules = AssistantPrompt.IgnoranceRules;
        var section = rules[rules.IndexOf("2D.", StringComparison.Ordinal)..];

        section.Should().Contain("THIS IS NOT 2C");
        section.Should().Contain("NOT that the data does not exist");
        section.Should().Contain("could not find it");
    }

    [Fact]
    public void The_fourth_category_apologises_and_offers_what_is_possible()
    {
        var rules = AssistantPrompt.IgnoranceRules;
        var section = rules[rules.IndexOf("2D.", StringComparison.Ordinal)..];

        section.Should().Contain("apologise");
        section.Should().Contain("what you CAN look up");
    }

    // Not a roadmap. "Yet" is honest about today; a date or a promise is a commitment the assistant has no
    // standing to make, and rule 2 already forbids inventing features.
    [Fact]
    public void The_fourth_category_promises_nothing()
    {
        var rules = AssistantPrompt.IgnoranceRules;
        var section = rules[rules.IndexOf("2D.", StringComparison.Ordinal)..];

        section.Should().Contain("do not give a date");
    }

    // ── The inventory that makes 2D decidable ────────────────────────────────

    [Fact]
    public void The_capability_inventory_reaches_the_model()
    {
        // ConfinementRules is what the answering model actually receives; IgnoranceRules is folded into
        // it, and the inventory travels inside IgnoranceRules so the two can never drift apart.
        AssistantPrompt.IgnoranceRules.Should().Contain(AssistantPrompt.CapabilityInventory);
        AssistantPrompt.ConfinementRules.Should().Contain(AssistantPrompt.CapabilityInventory);
    }

    // ★ THE DIRECTION IS THE POINT. "Plans of a payee" and "payees of a plan" are one word apart and only
    // the first exists; an inventory that named the tools without their direction would have prevented
    // nothing, because the mis-routed call looked perfectly reasonable.
    [Fact]
    public void The_inventory_states_the_direction_of_the_assignment_lookup()
    {
        AssistantPrompt.CapabilityInventory.Should().Contain("PAYEE to PLANS");
        AssistantPrompt.CapabilityInventory.Should().Contain("NO lookup that goes the other way");
        AssistantPrompt.CapabilityInventory.Should().Contain("cannot list the payees on a plan");
    }

    // The inventory routes the gap to 2D by name, so the model does not have to infer which category a
    // missing capability belongs to.
    [Fact]
    public void The_inventory_routes_a_gap_to_the_fourth_category_and_away_from_the_third()
    {
        AssistantPrompt.CapabilityInventory.Should().Contain("scenario 2D and never 2C");
    }

    // One line per registered tool (Infrastructure DependencyInjection registers exactly four). A
    // capability listed here that does not exist is an invented feature; one missing sends an answerable
    // question into 2D.
    [Fact]
    public void The_inventory_lists_the_four_real_lookups()
    {
        var inventory = AssistantPrompt.CapabilityInventory;

        inventory.Should().Contain("exactly four lookups");
        inventory.Should().Contain("ONE TRANSACTION");
        inventory.Should().Contain("CONFIGURATION");
        inventory.Should().Contain("BALANCE");
        inventory.Should().Contain("PLAN ASSIGNMENTS");
    }

    // ── The dispatcher must not mis-route in the first place ─────────────────

    // ★ WITHOUT THIS, 2D IS UNREACHABLE FOR THE CASE THAT CAUSED IT. The answering model only gets to
    // choose a category when no lookup ran. Let the dispatcher hand a payee tool a plan identifier and it
    // comes back with a genuine not-found — at which point rule 9 and 2C are correct, and the honest
    // answer never gets a chance. The two halves of this fix only work together.
    [Fact]
    public void The_dispatcher_is_told_not_to_send_a_plan_to_the_payee_tools()
    {
        var instructions = AssistantToolRunner.SelectionInstructions;

        instructions.Should().Contain("NO tool that lists");
        instructions.Should().Contain("PAYEE to PLANS");
        instructions.Should().Contain("Call NO tool for that question");
    }

    // The existing guard is the mirror image — a person's name must not go to the plan tool. Both
    // directions have to be closed; only one of them was.
    [Fact]
    public void The_dispatcher_keeps_the_guard_it_already_had_in_the_other_direction()
    {
        AssistantToolRunner.SelectionInstructions
            .Should().Contain("NEVER pass a PERSON'S NAME as a plan name");
    }

    // ── What must NOT have changed ───────────────────────────────────────────

    // ★ Rule 3's indistinguishable refusal was not the problem here, and relaxing it would make ids
    // probeable: "does not exist" and "not yours" must keep reading identically.
    [Fact]
    public void The_refusals_stay_indistinguishable()
    {
        Wasnie.Application.Assistant.Tools.GetPlanRulesTool.RefusalMessage
            .Should().Contain("or you do not have access to it");
        Wasnie.Application.Assistant.Tools.GetPayeePlansTool.RefusalMessage
            .Should().Contain("or you do not have access to");
    }

    // 2C still exists and still asks for a corrected identifier: a real lookup that really found nothing
    // is a different situation, and the fix must not have collapsed the two.
    [Fact]
    public void The_third_category_still_asks_for_a_corrected_identifier()
    {
        var rules = AssistantPrompt.IgnoranceRules;
        var third = rules[rules.IndexOf("2C.", StringComparison.Ordinal)..rules.IndexOf("2D.", StringComparison.Ordinal)];

        third.Should().Contain("THE LOOKUP FOUND NOTHING");
        third.Should().Contain("give you the exact name or id");
    }
}
