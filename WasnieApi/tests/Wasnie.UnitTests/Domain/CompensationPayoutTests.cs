using FluentAssertions;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Events;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.UnitTests.Domain;

public sealed class CompensationPayoutTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PayeeId = Guid.NewGuid();
    private static readonly Guid PlanId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly PayeeReference Snapshot =
        PayeeReference.Snapshot(PayeeId, "Ada Agnieszka", "EMP-301");

    private static readonly DateRange Period =
        DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

    private static PayoutLineSpec MakeLineSpec(decimal commission, string currency = "EUR") =>
        new(
            CreditId: Guid.NewGuid(),
            RuleId: Guid.NewGuid(),
            RuleName: "Base Commission",
            BaseAmount: Money.Of(commission * 10, currency),
            CommissionAmount: Money.Of(commission, currency),
            AppliedModifiers: []);

    private static CompensationPayout Calculate(
        IReadOnlyList<PayoutLineSpec>? specs = null,
        string fallbackCurrency = "EUR") =>
        CompensationPayout.Calculate(
            TenantId, PayeeId, PlanId, Snapshot, Period,
            specs ?? [MakeLineSpec(100m)],
            fallbackCurrency,
            "test-user",
            Guid.NewGuid(), Now, Guid.NewGuid(),
            () => Guid.NewGuid());

    // ── Calculate factory ─────────────────────────────────────────────────────

    [Fact]
    public void Calculate_ValidArgs_ReturnsCalculatedStatus()
    {
        var payout = Calculate();

        payout.Status.Should().Be(CompensationPayoutStatus.Calculated);
        payout.TenantId.Should().Be(TenantId);
        payout.PayeeId.Should().Be(PayeeId);
        payout.PlanId.Should().Be(PlanId);
    }

    [Fact]
    public void Calculate_SingleLine_TotalCommissionEqualsLineCommission()
    {
        var payout = Calculate([MakeLineSpec(342.76m)]);

        payout.TotalCommission.Amount.Should().Be(342.76m);
        payout.TotalCommission.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Calculate_MultipleLines_TotalCommissionIsSumOfAll()
    {
        var specs = new[]
        {
            MakeLineSpec(100m),
            MakeLineSpec(200m),
            MakeLineSpec(50.50m)
        };

        var payout = Calculate(specs);

        payout.TotalCommission.Amount.Should().Be(350.50m);
        payout.Lines.Should().HaveCount(3);
    }

    [Fact]
    public void Calculate_NoLines_TotalCommissionIsZero()
    {
        var payout = Calculate([], fallbackCurrency: "EUR");

        payout.TotalCommission.Amount.Should().Be(0m);
        payout.TotalCommission.Currency.Should().Be("EUR");
        payout.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_NoLines_UsesFallbackCurrencyNotUsd()
    {
        var payout = Calculate([], fallbackCurrency: "PLN");

        payout.TotalCommission.Currency.Should().Be("PLN");
        payout.TotalCommission.Amount.Should().Be(0m);
    }

    [Fact]
    public void Calculate_EmptyFallbackCurrency_ThrowsDomainException()
    {
        var act = () => Calculate([], fallbackCurrency: "");

        act.Should().Throw<DomainException>()
            .WithMessage("*currency*");
    }

    [Fact]
    public void Calculate_WhitespaceFallbackCurrency_ThrowsDomainException()
    {
        var act = () => Calculate([], fallbackCurrency: "  ");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Calculate_RaisesPayoutCalculatedEvent()
    {
        var payout = Calculate();

        payout.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PayoutCalculatedEvent>();
    }

    [Fact]
    public void Calculate_Lines_HaveCorrectData()
    {
        var spec = MakeLineSpec(150m);
        var payout = Calculate([spec]);

        var line = payout.Lines.Single();
        line.CreditId.Should().Be(spec.CreditId);
        line.RuleId.Should().Be(spec.RuleId);
        line.RuleName.Should().Be("Base Commission");
        line.CommissionAmount.Amount.Should().Be(150m);
        line.BaseAmount.Amount.Should().Be(1500m);
    }

    // ── Approve ───────────────────────────────────────────────────────────────

    [Fact]
    public void Approve_FromCalculated_TransitionsToApproved()
    {
        var payout = Calculate();

        payout.Approve("approver", Now.AddHours(1), Guid.NewGuid());

        payout.Status.Should().Be(CompensationPayoutStatus.Approved);
        payout.UpdatedBy.Should().Be("approver");
    }

    [Fact]
    public void Approve_FromCalculated_RaisesPayoutApprovedEvent()
    {
        var payout = Calculate();

        payout.Approve("approver", Now.AddHours(1), Guid.NewGuid());

        payout.DomainEvents.Should().Contain(e => e is PayoutApprovedEvent);
    }

    [Fact]
    public void Approve_FromApproved_ThrowsDomainException()
    {
        var payout = Calculate();
        payout.Approve("approver", Now.AddHours(1), Guid.NewGuid());

        var act = () => payout.Approve("approver2", Now.AddHours(2), Guid.NewGuid());

        act.Should().Throw<DomainException>()
            .WithMessage("*Calculated*");
    }

    [Fact]
    public void Approve_FromPaid_ThrowsDomainException()
    {
        var payout = Calculate();
        payout.Approve("approver", Now.AddHours(1), Guid.NewGuid());
        payout.MarkPaid("finance", Now.AddHours(2));

        var act = () => payout.Approve("approver2", Now.AddHours(3), Guid.NewGuid());

        act.Should().Throw<DomainException>();
    }

    // ── MarkPaid ──────────────────────────────────────────────────────────────

    [Fact]
    public void MarkPaid_FromApproved_TransitionsToPaid()
    {
        var payout = Calculate();
        payout.Approve("approver", Now.AddHours(1), Guid.NewGuid());

        payout.MarkPaid("finance", Now.AddHours(2));

        payout.Status.Should().Be(CompensationPayoutStatus.Paid);
    }

    [Fact]
    public void MarkPaid_FromCalculated_ThrowsDomainException()
    {
        var payout = Calculate();

        var act = () => payout.MarkPaid("finance", Now.AddHours(1));

        act.Should().Throw<DomainException>()
            .WithMessage("*Approved*");
    }

    [Fact]
    public void MarkPaid_FromPaid_ThrowsDomainException()
    {
        var payout = Calculate();
        payout.Approve("approver", Now.AddHours(1), Guid.NewGuid());
        payout.MarkPaid("finance", Now.AddHours(2));

        var act = () => payout.MarkPaid("finance2", Now.AddHours(3));

        act.Should().Throw<DomainException>();
    }

    // ── Dispute ───────────────────────────────────────────────────────────────

    [Fact]
    public void Dispute_FromCalculated_TransitionsToDisputed()
    {
        var payout = Calculate();

        payout.Dispute("payee", Now.AddHours(1));

        payout.Status.Should().Be(CompensationPayoutStatus.Disputed);
    }

    [Fact]
    public void Dispute_FromApproved_TransitionsToDisputed()
    {
        var payout = Calculate();
        payout.Approve("approver", Now.AddHours(1), Guid.NewGuid());

        payout.Dispute("payee", Now.AddHours(2));

        payout.Status.Should().Be(CompensationPayoutStatus.Disputed);
    }

    [Fact]
    public void Dispute_FromPaid_ThrowsDomainException()
    {
        var payout = Calculate();
        payout.Approve("approver", Now.AddHours(1), Guid.NewGuid());
        payout.MarkPaid("finance", Now.AddHours(2));

        var act = () => payout.Dispute("payee", Now.AddHours(3));

        act.Should().Throw<DomainException>()
            .WithMessage("*Paid*");
    }

    // ── Period ────────────────────────────────────────────────────────────────

    [Fact]
    public void Calculate_PeriodIsPersistedCorrectly()
    {
        var payout = Calculate();

        payout.Period.Start.Should().Be(new DateOnly(2026, 1, 1));
        payout.Period.End.Should().Be(new DateOnly(2026, 3, 31));
    }

    [Fact]
    public void Calculate_PayeeSnapshotIsPreserved()
    {
        var payout = Calculate();

        payout.PayeeSnapshot.FullName.Should().Be("Ada Agnieszka");
        payout.PayeeSnapshot.EmployeeCode.Should().Be("EMP-301");
    }
}
