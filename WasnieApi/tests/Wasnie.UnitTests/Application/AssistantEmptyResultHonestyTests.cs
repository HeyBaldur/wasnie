using FluentAssertions;
using Wasnie.Application.Assistant.Common;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// ★★ AN EMPTY RESULT IS NOT PROOF THAT SOMETHING DOES NOT EXIST.
///
/// The conversation that produced this file: asked whether a payee had any plans assigned, the
/// assistant answered that it could not find any and asked the user to check the spelling of the
/// name — while the payee's own screen, on the same monitor, listed one assignment (Q3 2026 — Plan
/// Comercial EMEA, current). It then quoted the reason back to the customer as the literal string
/// <c>NoAssignmentsOrNotVisible</c>, and when the user said "do you not understand my question?" it
/// replied by explaining that it was an artificial intelligence running on language-processing
/// infrastructure — and never returned to the plans.
///
/// Three separate failures, and only one of them was about wording:
///
///   1. The TOOL could not tell "has none" from "you may not see them" — one outcome token covered
///      both. That is fixed in <c>GetPayeePlansTool</c>, not here, and it had to be: no instruction
///      can make a model honest about a distinction the data does not carry. These tests pin the
///      rules that were only implementable once it did.
///   2. An internal identifier reached a paying customer's screen.
///   3. The identity block answered a complaint nobody had turned into a question about identity.
///
/// ★ WHAT THESE CAN AND CANNOT PROVE — the limit <c>AssistantConfinementTests</c> states. They pin
/// what REACHES the model, which is deterministic; whether it obeys is a property of somebody else's
/// weights and is judged on screen. Their real value is as regression tests against a FUTURE EDIT:
/// each rule below was written in response to a specific sentence a real user read, and the thing a
/// test can genuinely hold is that the rule does not quietly go away.
/// </summary>
public sealed class AssistantEmptyResultHonestyTests
{
    private const string SampleDoc = "## 15. Clawback\nIncentra records a debt against the payee.";

    /// <summary>
    /// The prompt as it is assembled when a lookup ran — which is the only shape in which the empty
    /// result rules are ever read. The token dictionaries ride with the data block, so a variant
    /// without one is not a variant these rules exist in.
    /// </summary>
    private static string WithData() =>
        AssistantPrompt.BuildSystemMessage(
            SampleDoc, documentationAvailable: true, navigationMap: string.Empty,
            toolData: """{"outcome":"NoAssignments","found":true}""");

    // ── The four outcomes are taught as four, not as three ────────────────────

    [Fact]
    public void THE_PROMPT_STILL_FITS_INSIDE_THE_PROVIDERS_ALLOWANCE()
    {
        // ★★ THE CONSTRAINT THIS WORK ITEM SPENT MOST OF ITS BUDGET AGAINST, recorded here so the next
        // person meets it before their rules are written rather than after. Every prompt rule is paid
        // for on EVERY request: the first design of this feature sent ~15,300 tokens and the provider
        // refused all of them with a 413 against a ~12,000-per-minute allowance.
        //
        // AssistantRoutingTests pins the ceiling for the assembled prompt. This asserts the same thing
        // one level down, on the two constants this work item actually grew, so a failure says WHICH
        // rules got expensive instead of only that the total did.
        //
        // ★ AND THE HEADROOM IS NOW THIN. Two of the rules added here (the identity boundary, 2C-i)
        // ride on every request including ones where no lookup runs; the leak rule and the money rule
        // were moved into DataReminder precisely because they cannot apply without one. The next rule
        // that belongs on the always-on path will not fit, and the honest options at that point are to
        // shorten an existing rule or to route the new one behind a condition — not to raise the
        // ceiling, which is the provider's number rather than ours.
        (AssistantPrompt.IdentityRules.Length / 4).Should().BeLessThan(1_180);
        (AssistantPrompt.IgnoranceRules.Length / 4).Should().BeLessThan(1_700);
    }

    [Fact]
    public void The_payee_plans_dictionary_names_all_four_outcomes()
    {
        // ★ THE SPLIT IS THE WHOLE FIX, AND THE PROMPT HAS TO KNOW ABOUT IT. A model taught three
        // branches for a payload that now has four will route the fourth by resemblance — and the
        // nearest neighbour of "AssignmentsNotVisible" is "NoAssignments", which is the exact wrong
        // answer this work item exists to end.
        var rules = AssistantPrompt.PayeePlansTokenRules;

        rules.Should().Contain("\"PayeePlans\"");
        rules.Should().Contain("\"NoAssignments\"");
        rules.Should().Contain("\"AssignmentsNotVisible\"");
        rules.Should().Contain("\"NotFoundOrNotVisible\"");

        // And the token that used to cover two opposite facts is gone from the instructions entirely.
        rules.Should().NotContain("NoAssignmentsOrNotVisible");
    }

    [Fact]
    public void A_VERIFIED_NOTHING_MAY_BE_REPORTED_AS_NOTHING()
    {
        // The other half of honesty, and the half that is easy to lose while fixing the first. If every
        // empty answer became "I cannot tell", the assistant would be useless for the ordinary, true
        // case: this user CAN see the assignments and there are none. It is allowed to say so.
        AssistantPrompt.PayeePlansTokenRules.Should()
            .Contain("22b.").And
            .Contain("CHECKED NOTHING AND YOU MAY REPORT IT AS ONE");
    }

    [Fact]
    public void BUT_ONLY_AS_FAR_AS_THE_QUERY_ACTUALLY_LOOKED()
    {
        // ★ "No active plan" and "never had a plan" are different claims, and only one of them was
        // asked for. includedEnded is what separates them, so the rule names the flag rather than
        // trusting the model to remember that the default filter narrowed the question.
        AssistantPrompt.PayeePlansTokenRules.Should()
            .Contain("includedEnded").And
            .Contain("RIGHT NOW");
    }

    [Fact]
    public void A_PERMISSION_WALL_IS_NEVER_REPORTED_AS_AN_ABSENCE()
    {
        // ★★ THE SENTENCE THE USER ACTUALLY READ, forbidden by name. "No tiene planes asignados" about
        // a payee whose assignments are merely hidden is a false statement about whether a real person
        // is covered by a commission plan.
        var rules = AssistantPrompt.PayeePlansTokenRules;

        rules.Should().Contain("22c.");
        rules.Should().Contain("YOU NEVER LOOKED");
        rules.Should().Contain("they are not assigned to any plan");
        rules.Should().Contain("they have no plan");
    }

    [Fact]
    public void The_restricted_answer_sends_the_user_where_the_truth_IS()
    {
        // ★ A REFUSAL THAT ENDS IN A DEAD END IS HALF AN ANSWER. The payee screen shows the assignments
        // to whoever may see them, so pointing at it is both true and actionable — and it is what the
        // user in the reported conversation was looking at the whole time.
        AssistantPrompt.PayeePlansTokenRules.Should().Contain("payee's own screen");
    }

    // ── MONEY-CLOSED ──────────────────────────────────────────────────────────

    [Fact]
    public void A_NEGATIVE_ABOUT_MONEY_MAY_NEVER_BE_DERIVED_FROM_AN_EMPTY_RESULT()
    {
        // ★★ THE RULE THIS PRODUCT'S DOMAIN DEMANDS. "This payee has no plan / no quota / no
        // commission / no balance" is a statement an administrator can act on: stop a payment, open an
        // investigation, rebuild an assignment that already exists. So it needs positive evidence, and
        // an empty result is the absence of evidence rather than evidence of absence.
        var rules = AssistantPrompt.PayeePlansTokenRules;

        rules.Should().Contain("22c-i.");
        rules.Should().Contain("no plan / quota / commission / balance");
        rules.Should().Contain("positively established it");
        rules.Should().Contain("never").And.Contain("from an empty result");
    }

    [Fact]
    public void An_answer_with_no_evidence_says_WHAT_IT_DID_instead()
    {
        // The replacement behaviour, not just the prohibition. A rule that only forbids leaves the
        // model to invent the alternative, and what it invented last time was blaming the user.
        AssistantPrompt.PayeePlansTokenRules.Should()
            .Contain("describe WHAT YOU DID").And
            .Contain("full name and employee code");
    }

    // ── Blame, and asking for what it already has ─────────────────────────────

    [Fact]
    public void THE_ASSISTANT_DOES_NOT_ASK_FOR_AN_IDENTIFIER_IT_ALREADY_PRINTED()
    {
        // ★ THE REPORTED TURN. One message earlier the assistant had itself written the payee's full
        // name and employee code; the next message asked the user to supply the payee's identifier.
        // The rule lives in 2C because that is the scenario that asks for identifiers at all.
        var rules = AssistantPrompt.IgnoranceRules;

        rules.Should().Contain("2C-i.");
        rules.Should().Contain("CHECK WHETHER YOU ALREADY HAVE THE IDENTIFIER");
        rules.Should().Contain("Payees, plans and transactions");
    }

    [Fact]
    public void THE_USERS_SPELLING_IS_NOT_BLAMED_FOR_A_SEARCH_THE_ASSISTANT_WORDED()
    {
        // "Revise que el nombre sea exacto" is a fair request when the user typed the name. It is not
        // one when the assistant chose the search term itself, and a user told to check their spelling
        // for a search they never worded loses more trust than the empty result ever cost.
        AssistantPrompt.IgnoranceRules.Should()
            .Contain("do not make it their fault").And
            .Contain("fair when THEY typed the name, not when you did");

        // The same rule, restated where the outcome is actually branched on.
        AssistantPrompt.PayeePlansTokenRules.Should().Contain("blaming their spelling");
    }

    // ── Nothing from inside the machine reaches the page ──────────────────────

    [Fact]
    public void NO_INTERNAL_IDENTIFIER_MAY_APPEAR_IN_AN_ANSWER()
    {
        // ★★ IT REACHED A PAYING CUSTOMER'S SCREEN. Rule 18 already forbade printing tokens, but it
        // sits inside the PLAN-RULES dictionary and lists only plan-rule tokens, so it read as advice
        // about rate semantics. The general rule now lives with the data rules, where every lookup can
        // see it, and it names outcome values first because that is the one that leaked.
        var rules = AssistantPrompt.DataRules;

        rules.Should().Contain("10a.");
        rules.Should().Contain("NOTHING FROM INSIDE THE MACHINE APPEARS IN YOUR ANSWER");
        rules.Should().Contain("Outcome");
        rules.Should().Contain("already reached a customer's screen",
            "the rule states that this happened, so a future reader knows it is not hypothetical");

        // ★ AND IT NAMES THE SHAPE OF THE THING, so a token invented next year is covered too.
        rules.Should().Contain("field").And.Contain("enum values").And.Contain("status tokens");
    }

    [Fact]
    public void The_leak_rule_survives_into_the_assembled_prompt()
    {
        // The dictionaries are composed, and a rule that exists only on a constant nobody concatenates
        // is a rule the model never reads.
        WithData().Should().Contain("NOTHING FROM INSIDE THE MACHINE APPEARS IN YOUR ANSWER");
    }

    [Fact]
    public void The_reminder_repeats_both_promises_at_the_position_that_carries_most_weight()
    {
        // ★ LAST READ, HEAVIEST WEIGHT. Fifteen thousand tokens of documentation separate rule 10a
        // from the user's question; the reminder is what keeps it from being buried by the material it
        // applies to. The same reasoning the classification restatement already relies on.
        var prompt = WithData();

        prompt.Should().Contain("never print an outcome value, a field name or any internal identifier");
        prompt.Should().Contain("quota, commission or balance unless the lookup established it");

        // ★ AND IT IS ABSENT WHERE IT CANNOT APPLY. A documentation question runs no lookup, so it has
        // no outcome value to leak and no empty result to over-read — carrying these sentences there
        // would spend the routing budget on rules that cannot fire.
        AssistantPrompt.BuildSystemMessage(SampleDoc).Should()
            .NotContain("never print an outcome value");
    }

    // ── Regression: the categories that were already right ────────────────────

    [Fact]
    public void The_missing_capability_scenario_is_untouched()
    {
        // ★ THE TURN THAT WORKED, AND MUST KEEP WORKING. Asked for Quota Attainment, the assistant
        // correctly said the capability does not exist and listed the four that do. That is 2D, it is
        // a different kind of not-knowing from an empty result, and nothing in this work item may
        // blur the two — 2D is "I cannot look", 2C is "I looked and found nothing", and the new rules
        // add a third, "I looked and am not allowed to see".
        var rules = AssistantPrompt.IgnoranceRules;

        rules.Should().Contain("2D. THE CAPABILITY DOES NOT EXIST YET");
        rules.Should().Contain("THIS IS NOT 2C, AND CONFUSING THE TWO IS THE MOST DAMAGING ANSWER");
    }
}
