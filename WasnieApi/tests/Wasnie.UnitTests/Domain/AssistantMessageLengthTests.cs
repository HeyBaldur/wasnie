using FluentAssertions;
using Wasnie.Application.Assistant.Common;
using Wasnie.Domain.Assistant;
using Wasnie.Domain.Exceptions;

namespace Wasnie.UnitTests.Domain;

/// <summary>
/// A user's message and an assistant's reply have DIFFERENT limits (2026-09-15): one limit for both cut long answers to
/// the size of what a person types, in silence.
/// </summary>
public sealed class AssistantMessageLengthTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 16, 0, 0, TimeSpan.Zero);

    private static AssistantMessage Create(AssistantMessageRole role, int length) =>
        AssistantMessage.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), role, new string('a', length), 0, Now);

    [Fact]
    public void A_reply_longer_than_a_users_message_is_accepted()
    {
        Create(AssistantMessageRole.Assistant, 12_000).Content.Should().HaveLength(12_000);
    }

    [Fact]
    public void A_users_message_keeps_its_own_limit()
    {
        var act = () => Create(AssistantMessageRole.User, AssistantMessage.MaxContentLength + 1);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_reply_past_the_reply_limit_is_refused()
    {
        var act = () => Create(AssistantMessageRole.Assistant, AssistantMessage.MaxReplyLength + 1);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void The_reply_limit_equals_the_degeneration_guards_ceiling()
    {
        // ★ Kept equal on purpose: the guard refuses any answer past its ceiling, so every answer that reaches the store
        // fits. If one moves without the other, answers are either cut again or refused for no reason.
        AssistantMessage.MaxReplyLength.Should().Be(DegenerationGuard.DefaultMaxCharacters);
    }
}
