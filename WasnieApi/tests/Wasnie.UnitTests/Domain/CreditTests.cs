using FluentAssertions;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Events;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.UnitTests.Domain;

public sealed class CreditTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Credit AllocateCredit() => Credit.Allocate(
        tenantId: Guid.NewGuid(),
        transactionId: Guid.NewGuid(),
        payeeId: Guid.NewGuid(),
        planId: Guid.NewGuid(),
        ruleId: Guid.NewGuid(),
        ruleSnapshot: RuleSnapshot.Freeze(
            Guid.NewGuid(), Guid.NewGuid(), 1, "Base Commission",
            RateTable.Flat(0.05m), Trigger.Always(), Now),
        originalAmount: Money.Of(1000m, "EUR"),
        creditedAmount: Money.Of(50m, "EUR"),
        splitPercentage: Percentage.FromPercent(100m),
        role: CreditRole.Primary,
        allocatedBy: "test-user",
        id: Guid.NewGuid(),
        now: Now,
        eventId: Guid.NewGuid());

    // ── WI-CALC-A.0: SupersededAt / SupersededBy default null ────────────────

    [Fact]
    public void Allocate_SupersededAtIsNull()
    {
        var credit = AllocateCredit();
        credit.SupersededAt.Should().BeNull();
    }

    [Fact]
    public void Allocate_SupersededByIsNull()
    {
        var credit = AllocateCredit();
        credit.SupersededBy.Should().BeNull();
    }

    // ── WI-CALC-A.2: Credit.Supersede (Decision #46 Case A) ──────────────────

    [Fact]
    public void Supersede_SetsSupersededAtAndRaisesEvent()
    {
        var credit = AllocateCredit();
        credit.ClearDomainEvents();

        credit.Supersede("Reassigned to another payee", Now, Guid.NewGuid());

        credit.SupersededAt.Should().Be(Now);
        credit.SupersededBy.Should().Be("Reassigned to another payee");
        credit.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<CreditSupersededEvent>();
    }

    [Fact]
    public void Supersede_EventCarriesCorrectFields()
    {
        var credit = AllocateCredit();
        credit.ClearDomainEvents();
        var eventId = Guid.NewGuid();

        credit.Supersede("reason for supersede here", Now, eventId);

        var evt = credit.DomainEvents.OfType<CreditSupersededEvent>().Single();
        evt.EventId.Should().Be(eventId);
        evt.OccurredOn.Should().Be(Now);
        evt.CreditId.Should().Be(credit.Id);
        evt.TransactionId.Should().Be(credit.TransactionId);
        evt.PayeeId.Should().Be(credit.PayeeId);
        evt.TenantId.Should().Be(credit.TenantId);
        evt.Reason.Should().Be("reason for supersede here");
    }

    [Fact]
    public void Supersede_WhenAlreadySuperseded_ThrowsDomainException()
    {
        var credit = AllocateCredit();
        credit.Supersede("first supersede", Now, Guid.NewGuid());
        credit.ClearDomainEvents();

        var act = () => credit.Supersede("second supersede", Now.AddMinutes(1), Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("*already superseded*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public void Supersede_EmptyOrWhitespaceReason_ThrowsDomainException(string? reason)
    {
        var credit = AllocateCredit();

        var act = () => credit.Supersede(reason!, Now, Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("*reason is required*");
    }

    [Fact]
    public void Supersede_ReasonExceeds500Chars_ThrowsDomainException()
    {
        var credit = AllocateCredit();
        var longReason = new string('x', 501);

        var act = () => credit.Supersede(longReason, Now, Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("*500*");
    }

    [Fact]
    public void Supersede_ReasonExactly500Chars_Succeeds()
    {
        var credit = AllocateCredit();
        var exactReason = new string('x', 500);

        var act = () => credit.Supersede(exactReason, Now, Guid.NewGuid());

        act.Should().NotThrow();
        credit.SupersededBy.Should().HaveLength(500);
    }
}
