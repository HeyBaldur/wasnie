using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Assistant.Common;
using Wasnie.Infrastructure.Services.Assistant;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// A clawback balance in the payee's favour is named, not merged.
///
/// ★★ THE CONVERSATION THAT PRODUCED THIS. A user asked for a payee's balance and was told 1,280 EUR
/// were pending. They ran the pay run the assistant recommended. The figure did not move. They said the
/// payouts screen was empty; the assistant told them to check payouts again, then to untick a filter,
/// then to run another pay run. Four turns prescribing the wrong subsystem.
///
/// ★ AND THE ASSISTANT WAS READING THE PAYLOAD CORRECTLY. A positive PayeeBalance — money owed TO the
/// payee because more was withheld than was owed — has always been added into `awaitingPayment`
/// (GetPayeeLedgerSummaryHandler), and rule 21 teaches that `awaitingPayment` is "everything earned and
/// not yet paid". So a clawback credit arrived indistinguishable from an unpaid commission. The defect
/// was in the FIELD, not in the model: one number meant two things and a pay run can only settle one of
/// them.
///
/// The fix is a breakdown, not a correction — the total is untouched, so nothing that reads
/// `awaitingPayment` or `netPendingPayout` shifts.
/// </summary>
public sealed class AssistantClawbackCreditTests
{
    // ══ The prompt half ═══════════════════════════════════════════════════════

    [Fact]
    public void THE_MODEL_IS_TOLD_A_PAY_RUN_CANNOT_MOVE_A_CLAWBACK_CREDIT()
    {
        // ★ THE ONE SENTENCE THAT ENDS THE REPORTED LOOP. Without it the model has the number and no
        // reason to treat it differently from the rest of awaitingPayment — which is exactly how it
        // got to "run another pay run".
        var rules = AssistantPrompt.BalanceTokenRules;

        rules.Should().Contain("clawbackCredit");
        rules.Should().Contain("PAY RUN CANNOT MOVE");
        rules.Should().Contain("Clawback");
        rules.Should().Contain("NEVER prescribe a pay run for it");
    }

    [Fact]
    public void It_is_stated_as_PART_OF_awaitingPayment_rather_than_as_a_separate_total()
    {
        // ★ BECAUSE THE TOTAL DID NOT CHANGE. If the model read clawbackCredit as something to ADD to
        // awaitingPayment it would double-count the money — the opposite error, and just as wrong.
        AssistantPrompt.BalanceTokenRules.Should().Contain("IS PART OF awaitingPayment");
    }

    // ══ ★ The contradiction rule ══════════════════════════════════════════════

    [Fact]
    public void WHEN_THE_USER_SAYS_THEY_LOOKED_AND_IT_IS_NOT_THERE_THAT_IS_EVIDENCE()
    {
        // ★★ THE DEFECT THAT IS NOT ABOUT CLAWBACK AT ALL. Three times the user reported the payouts
        // were empty; three times the answer was more payout steps. There will always be a subsystem
        // the assistant does not know about — what has to change is the reflex of repeating the
        // strongest instruction when the user reports it did not work.
        var rules = AssistantPrompt.DataRules;

        rules.Should().Contain("10c.");
        rules.Should().Contain("THEY ARE RIGHT AND YOU");
        rules.Should().Contain("Do NOT repeat the steps you already gave");
        rules.Should().Contain("your explanation was wrong");
    }

    [Fact]
    public void The_rule_forbids_insisting_specifically_about_MONEY()
    {
        AssistantPrompt.DataRules.Should()
            .Contain("Never insist on an explanation about").And
            .Contain("MONEY that the user has just told you is false");
    }

    [Fact]
    public void It_offers_an_alternative_or_admits_it_cannot_tell_rather_than_going_quiet()
    {
        // A rule that only forbids leaves the model to invent the replacement, and what it invented
        // last time was more of the same. Both exits are named.
        var rules = AssistantPrompt.DataRules;

        rules.Should().Contain("name a").And.Contain("different source");
        rules.Should().Contain("cannot determine it");
    }

    // ══ ★★ The arithmetic the assistant invented ═══════════════════════════

    [Fact]
    public void THE_MODEL_MAY_NOT_DERIVE_FIGURES_THE_LOOKUP_DID_NOT_RETURN()
    {
        // ★★ 78.500 ÷ 5 = 15.700 UNITS, PRESENTED AS A FACT. Asked about a rule showing "500%",
        // the assistant divided the transaction's BASE AMOUNT by the per-unit rate and announced that
        // 15,700 units had been sold. The line was one unit paying €5. Neither number in that division
        // was the one it thought it was, and the result does not exist anywhere in the data.
        var rules = AssistantPrompt.DataRules;

        rules.Should().Contain("10d.");
        rules.Should().Contain("DO NOT DO ARITHMETIC THE LOOKUP DID NOT DO");
        rules.Should().Contain("Never divide, multiply or add");
    }

    [Fact]
    public void It_says_what_to_do_INSTEAD_when_the_quantity_is_not_in_the_data()
    {
        // A rule that only forbids leaves the model to invent the replacement — which is how it got
        // here. "The lookup does not include that, and here is where to see it" is an answer.
        AssistantPrompt.DataRules.Should()
            .Contain("how many").And
            .Contain("does not include that and where to see it");
    }

    [Fact]
    public void EVERY_FIGURE_IS_NAMED_BY_THE_FIELD_IT_CAME_FROM()
    {
        // ★ THE ROOT OF THE INVENTED DIVISION: it treated a BASE AMOUNT as a commission. Naming the
        // field is what makes that mistake visible while it is being written rather than after.
        AssistantPrompt.DataRules.Should()
            .Contain("name every figure by the FIELD it came from").And
            .Contain("never treating a base amount as a commission");
    }

    // ══ The manual half ═══════════════════════════════════════════════════════

    [Fact]
    public void THE_GUIDE_EXPLAINS_THE_TWO_SOURCES_OF_A_PENDING_AMOUNT()
    {
        // ★ AND IT LIVES IN THE CLAWBACK SECTION, which is what makes it reachable: the router picks
        // sections by meaning, and "where does this pending amount come from" is a clawback question
        // once you know the answer. Asserted on the section's own text rather than on the whole file,
        // so a future move of the paragraph fails here instead of silently going unrouted.
        var knowledge = new FileAssistantKnowledgeBase(NullLogger<FileAssistantKnowledgeBase>.Instance);

        var clawback = knowledge.Sections.Single(s => s.Title.StartsWith("15. Clawback"));

        clawback.Text.Should().Contain("two possible sources");
        clawback.Text.Should().Contain("pay run does not settle a clawback balance");
        clawback.Text.Should().Contain("Clawback** tab");
    }

    [Fact]
    public void The_guide_says_a_POSITIVE_balance_is_money_owed_TO_the_payee()
    {
        // The sign is the whole concept, and it is the one thing a reader cannot guess: everywhere else
        // in this product a ledger balance going up is bad news for the payee.
        var knowledge = new FileAssistantKnowledgeBase(NullLogger<FileAssistantKnowledgeBase>.Instance);
        var clawback = knowledge.Sections.Single(s => s.Title.StartsWith("15. Clawback"));

        clawback.Text.Should().Contain("goes **positive**");
        clawback.Text.Should().Contain("owed **to** them");
    }

    [Fact]
    public void The_new_paragraph_did_not_smuggle_the_section_past_the_size_guard()
    {
        // ★ THE CONSTRAINT THIS WORK ITEM COLLIDED WITH. The corpus and the prompt share one budget, and
        // this paragraph is the reason the old guard finally fired. It is pinned small here so the next
        // person expanding it finds out from a test rather than from a 413.
        var knowledge = new FileAssistantKnowledgeBase(NullLogger<FileAssistantKnowledgeBase>.Instance);
        var clawback = knowledge.Sections.Single(s => s.Title.StartsWith("15. Clawback"));

        (clawback.Text.Replace("\r\n", "\n").Length / 4).Should().BeLessThan(2_500);
    }
}
