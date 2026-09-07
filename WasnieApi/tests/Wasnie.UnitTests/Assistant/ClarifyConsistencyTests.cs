using FluentAssertions;
using Wasnie.Application.Assistant.Common;

namespace Wasnie.UnitTests.Assistant;

/// <summary>
/// KAN-58, second round — the clarify form fired erratically, and this pins the two halves of the fix.
///
/// ★★ THE DEFECT AS REPORTED. Runtime verification sent the SAME input three times — the bare payee
/// name "Aleksandra" — and got three different behaviours: the clarify form once, a greeting addressed
/// to Aleksandra once (as though the user had introduced themselves), and a prose question once. An
/// acceptance criterion that says "consistently" cannot be met by something sampled.
///
/// ★★ WHAT A UNIT TEST CAN AND CANNOT PROVE HERE, STATED PLAINLY. The mechanical half — that the
/// dispatcher runs at temperature 0 — lives in the infrastructure payload and is asserted there by
/// construction, not here. What THIS file pins is the instruction half: that the dispatcher's prompt
/// actually contains the rules, in the words the fix depends on. It cannot prove the model obeys them.
/// Only a runtime pass against the provider can, and that is stated as an open item rather than
/// implied by a green suite (§A2).
/// </summary>
public sealed class ClarifyConsistencyTests
{
    private static string Instructions => AssistantToolRunner.SelectionInstructions;

    /// <summary>
    /// ★ THE GREETING CASE. "Aleksandra" was answered with "¡Hola Aleksandra!" — the dispatcher read a
    /// payee's name as the user introducing themselves. Nobody types a payee's name into a commission
    /// tool to say hello, and the instruction has to say so in those terms.
    /// </summary>
    [Fact]
    public void The_dispatcher_is_told_a_bare_name_is_never_a_greeting()
    {
        Instructions.Should().Contain("NEVER A GREETING");
        Instructions.Should().Contain("A BARE NAME IS A QUESTION ABOUT THAT RECORD");
    }

    /// <summary>★ And it is told what to DO with one, not only what not to do.</summary>
    [Fact]
    public void A_bare_name_is_routed_to_the_clarify_form_with_the_name_attached()
    {
        Instructions.Should().Contain("Call ask_user_to_choose with the functions that fit");
        Instructions.Should().Contain("pass the name through as the argument");
    }

    /// <summary>
    /// ★ THE FOURTH ROW OF THE VERIFICATION TABLE. "Show me the form" was read as a question about a
    /// record called "form" and answered with "which form, New Payee?". A request for the options is a
    /// request for the options.
    /// </summary>
    [Fact]
    public void Asking_for_the_options_is_routed_to_the_clarify_form()
    {
        Instructions.Should().Contain("WHEN THE USER ASKS FOR THE OPTIONS THEMSELVES");
        Instructions.Should().Contain("Show me the form");
        Instructions.Should().Contain("not a question about a record called");
    }

    /// <summary>
    /// ★★ THE THREE-WAY SPLIT STAYS INTACT (comentario del 11:25). Pinning the bare-name case must not
    /// have turned the dispatcher into something that offers a menu for everything: one clear tool
    /// still means call that tool, and NO relevant function still means no form at all.
    /// </summary>
    [Fact]
    public void The_three_cases_are_still_distinct()
    {
        Instructions.Should().Contain("ONE tool is clearly right");
        Instructions.Should().Contain("Never ask a question you could answer");
        Instructions.Should().Contain("NONE applies");
        Instructions.Should().Contain("call NO tool at all");
    }

    /// <summary>
    /// ★ AND THE ARGUMENT IS STILL NEVER INVENTED. The bare-name rule tells the dispatcher to pass a
    /// name through; this is the sentence that stops it passing through one the user never said, which
    /// would run a real lookup on made-up input.
    /// </summary>
    [Fact]
    public void The_option_argument_still_may_not_be_invented()
    {
        Instructions.Should().Contain("ONLY WHAT THE USER ACTUALLY SAID");
        Instructions.Should().Contain("about the wrong record");
    }
}
