using FluentAssertions;
using Wasnie.Application.Assistant.Common;
using Wasnie.Domain.Assistant;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// A conversation is named after the first thing said in it.
///
/// ★ WHAT THIS REPLACED: "Chat 2026-07-31 14:58". A history list of a dozen timestamps is a list you
/// have to open one by one — the name told the reader nothing they could use to find anything.
/// </summary>
public sealed class ConversationTitleTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 17, 0, 0, TimeSpan.Zero);

    private static AssistantConversation Untitled() =>
        AssistantConversation.Start(
            Guid.NewGuid(), Guid.NewGuid(), "user-alice", AssistantConversation.UntitledSentinel, Now);

    // ── Deriving the text ─────────────────────────────────────────────────────

    [Fact]
    public void A_short_message_becomes_the_title_verbatim()
    {
        ConversationTitle.FromMessage("¿Wasnie soporta clawbacks?")
            .Should().Be("¿Wasnie soporta clawbacks?");
    }

    [Fact]
    public void A_long_message_is_cut_at_a_word_boundary_with_an_ellipsis()
    {
        var title = ConversationTitle.FromMessage(
            "Necesito entender cómo funciona el clawback cuando un vendedor renuncia " +
            "y ya cobró las comisiones del trimestre anterior");

        title.Length.Should().BeLessThanOrEqualTo(ConversationTitle.MaxLength + 1);
        title.Should().EndWith("…");
        // Cut between words, never through one — a title ending "renun…" reads as a bug.
        title.TrimEnd('…').Should().NotEndWith(" ");
        title.Should().StartWith("Necesito entender cómo funciona");
    }

    [Fact]
    public void One_enormous_word_is_still_cut_rather_than_collapsing_the_title()
    {
        // The word-boundary rule must not fire when there is no useful boundary: a single long token
        // would otherwise leave a two-character title, or none.
        var title = ConversationTitle.FromMessage(new string('x', 200));

        title.Length.Should().BeGreaterThan(ConversationTitle.MaxLength / 2);
        title.Should().EndWith("…");
    }

    [Fact]
    public void Newlines_and_runs_of_whitespace_collapse_to_single_spaces()
    {
        // A title is one line in a list; a pasted multi-line question must not break the row.
        ConversationTitle.FromMessage("first line\n\nsecond    line\tthird")
            .Should().Be("first line second line third");
    }

    [Fact]
    public void Markdown_a_user_typed_is_stripped_rather_than_carried_into_the_list()
    {
        // `**urgent**` should read as "urgent" in the history, not carry its asterisks around forever.
        ConversationTitle.FromMessage("**urgente**: revisar `PlanStatus` en # planes")
            .Should().Be("urgente: revisar PlanStatus en planes");
    }

    [Fact]
    public void An_empty_or_whitespace_message_yields_no_title()
    {
        // The caller then leaves the thread untitled rather than naming it after nothing.
        ConversationTitle.FromMessage("").Should().BeEmpty();
        ConversationTitle.FromMessage("   \n  ").Should().BeEmpty();
        ConversationTitle.FromMessage(null).Should().BeEmpty();
    }

    // ── Applying it to the conversation ───────────────────────────────────────

    [Fact]
    public void A_new_conversation_starts_untitled_rather_than_stamped_with_the_time()
    {
        var conversation = Untitled();

        conversation.IsUntitled.Should().BeTrue();
        // ★ The sentinel is language-neutral: the client renders its own label, so "New conversation"
        // in English is never frozen into a row its owner reads in Spanish.
        conversation.Title.Should().Be(AssistantConversation.UntitledSentinel);
        conversation.Title.Should().NotContain("2026", "a timestamp is not a name");
    }

    [Fact]
    public void The_first_message_names_the_thread()
    {
        var conversation = Untitled();

        conversation.TitleFromFirstMessage(
            ConversationTitle.FromMessage("¿Cómo creo un plan de comisiones?"), Now);

        conversation.Title.Should().Be("¿Cómo creo un plan de comisiones?");
        conversation.IsUntitled.Should().BeFalse();
    }

    [Fact]
    public void The_second_message_does_NOT_rename_the_thread()
    {
        var conversation = Untitled();

        conversation.TitleFromFirstMessage("the first question", Now);
        conversation.TitleFromFirstMessage("a completely different second question", Now);

        conversation.Title.Should().Be("the first question", "only the first message names a thread");
    }

    [Fact]
    public void A_name_the_user_chose_outranks_a_derived_one()
    {
        // ★ Silently overwriting a name someone typed is the kind of small theft that makes a feature
        // untrustworthy.
        var conversation = Untitled();
        conversation.Rename("Q3 planning", Now);

        conversation.TitleFromFirstMessage("some message that would have become the title", Now);

        conversation.Title.Should().Be("Q3 planning");
    }

    [Fact]
    public void An_empty_derived_title_leaves_the_thread_untitled()
    {
        var conversation = Untitled();

        conversation.TitleFromFirstMessage(ConversationTitle.FromMessage("   "), Now);

        conversation.IsUntitled.Should().BeTrue("better unnamed than named after nothing");
    }
}
