using FluentAssertions;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Events;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.UnitTests.Domain;

public sealed class PayRunTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly PeriodStart = new(2026, 5, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 5, 31);

    private static PayRun MakeRun() =>
        PayRun.Open(TenantId, PeriodStart, PeriodEnd, "admin@test.com", Guid.NewGuid(), Now);

    // ── Open (factory) ────────────────────────────────────────────────────────

    [Fact]
    public void Open_ValidArgs_CreatesDraftRun()
    {
        var run = MakeRun();

        run.Status.Should().Be(PayRunStatus.Draft);
        run.TenantId.Should().Be(TenantId);
        run.PeriodStart.Should().Be(PeriodStart);
        run.PeriodEnd.Should().Be(PeriodEnd);
        run.CreatedAt.Should().Be(Now);
        run.CreatedBy.Should().Be("admin@test.com");
        run.ApprovedAt.Should().BeNull();
        run.PaidAt.Should().BeNull();
        run.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Open_StartAfterEnd_Throws()
    {
        var act = () => PayRun.Open(TenantId, PeriodEnd, PeriodStart, "a", Guid.NewGuid(), Now);
        act.Should().Throw<DomainException>().WithMessage("*PeriodStart*");
    }

    [Fact]
    public void Open_EmptyActor_Throws()
    {
        var act = () => PayRun.Open(TenantId, PeriodStart, PeriodEnd, "", Guid.NewGuid(), Now);
        act.Should().Throw<DomainException>().WithMessage("*CreatedBy*");
    }

    // ── Approve ───────────────────────────────────────────────────────────────

    [Fact]
    public void Approve_FromDraft_SetsApprovedAndRaisesEvent()
    {
        var run = MakeRun();
        var eventId = Guid.NewGuid();

        run.Approve("mgr@test.com", Now, eventId);

        run.Status.Should().Be(PayRunStatus.Approved);
        run.ApprovedAt.Should().Be(Now);
        run.ApprovedBy.Should().Be("mgr@test.com");
        run.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PayRunApprovedEvent>()
            .Which.PayRunId.Should().Be(run.Id);
    }

    [Fact]
    public void Approve_FromApproved_Throws()
    {
        var run = MakeRun();
        run.Approve("a", Now, Guid.NewGuid());

        var act = () => run.Approve("b", Now, Guid.NewGuid());
        act.Should().Throw<DomainException>().WithMessage("*Draft*");
    }

    [Fact]
    public void Approve_FromPaid_Throws()
    {
        var run = MakeRun();
        run.Approve("a", Now, Guid.NewGuid());
        run.MarkPaid("a", Now, Guid.NewGuid());

        var act = () => run.Approve("b", Now, Guid.NewGuid());
        act.Should().Throw<DomainException>();
    }

    // ── MarkPaid ──────────────────────────────────────────────────────────────

    [Fact]
    public void MarkPaid_FromApproved_SetsStatusAndRaisesEvent()
    {
        var run = MakeRun();
        run.Approve("a", Now, Guid.NewGuid());
        run.ClearDomainEvents();

        run.MarkPaid("payroll@test.com", Now, Guid.NewGuid());

        run.Status.Should().Be(PayRunStatus.Paid);
        run.PaidAt.Should().Be(Now);
        run.PaidBy.Should().Be("payroll@test.com");
        run.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<PayRunPaidEvent>();
    }

    [Fact]
    public void MarkPaid_FromDraft_Throws()
    {
        var run = MakeRun();
        var act = () => run.MarkPaid("a", Now, Guid.NewGuid());
        act.Should().Throw<DomainException>().WithMessage("*Approved*");
    }

    // ── Reopen ────────────────────────────────────────────────────────────────

    [Fact]
    public void Reopen_FromApproved_RevertsToDraftAndClearsApproval()
    {
        var run = MakeRun();
        run.Approve("a", Now, Guid.NewGuid());
        run.ClearDomainEvents();

        run.Reopen("admin@test.com", Now, Guid.NewGuid());

        run.Status.Should().Be(PayRunStatus.Draft);
        run.ApprovedAt.Should().BeNull();
        run.ApprovedBy.Should().BeNull();
        run.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<PayRunReopenedEvent>();
    }

    [Fact]
    public void Reopen_FromDraft_Throws()
    {
        var run = MakeRun();
        var act = () => run.Reopen("a", Now, Guid.NewGuid());
        act.Should().Throw<DomainException>().WithMessage("*Approved*");
    }

    [Fact]
    public void Reopen_FromPaid_Throws()
    {
        var run = MakeRun();
        run.Approve("a", Now, Guid.NewGuid());
        run.MarkPaid("a", Now, Guid.NewGuid());

        var act = () => run.Reopen("b", Now, Guid.NewGuid());
        act.Should().Throw<DomainException>().WithMessage("*Paid*");
    }

    // ── UpdateRollUps ─────────────────────────────────────────────────────────

    // Builds a minimal CompensationPayout for roll-up tests — only TotalCommission is needed.
    private static CompensationPayout MakePayout(decimal amount, string currency)
    {
        var payeeId = Guid.NewGuid();
        return CompensationPayout.Calculate(
            TenantId, payeeId, Guid.NewGuid(),
            PayeeReference.Snapshot(payeeId, "Test Payee", "EMP-001"),
            DateRange.Of(PeriodStart, PeriodEnd),
            amount > 0
                ? [new PayoutLineSpec(Guid.NewGuid(), Guid.NewGuid(), "Rule",
                    Money.Of(amount * 10, currency), Money.Of(amount, currency), [])]
                : [],
            currency, "system",
            Guid.NewGuid(), Now, Guid.NewGuid(), Guid.NewGuid);
    }

    [Fact]
    public void UpdateRollUps_SingleCurrency_ComputesCorrectCountsAndTotals()
    {
        var run = MakeRun();
        var payouts = new List<CompensationPayout>
        {
            MakePayout(1000m, "EUR"),
            MakePayout(500m, "EUR"),
            MakePayout(0m, "EUR"),   // zero payout
        };

        run.UpdateRollUps(payouts);

        run.PayeeCount.Should().Be(3);
        run.PaidPayeeCount.Should().Be(2);
        run.ZeroPayoutCount.Should().Be(1);
        run.TotalAmounts.Should().HaveCount(1);
        run.TotalAmounts["EUR"].Should().Be(1500m);
    }

    [Fact]
    public void UpdateRollUps_MultiCurrency_GroupsByCurrencyWithoutMixing()
    {
        var run = MakeRun();
        var payouts = new List<CompensationPayout>
        {
            MakePayout(95000m, "EUR"),
            MakePayout(87500m, "PLN"),
            MakePayout(0m, "EUR"),
        };

        run.UpdateRollUps(payouts);

        run.PayeeCount.Should().Be(3);
        run.PaidPayeeCount.Should().Be(2);
        run.ZeroPayoutCount.Should().Be(1);
        run.TotalAmounts.Should().HaveCount(2);
        run.TotalAmounts["EUR"].Should().Be(95000m);
        run.TotalAmounts["PLN"].Should().Be(87500m);
    }

    [Fact]
    public void UpdateRollUps_AllZero_ProducesZeroCountsAndEmptyTotals()
    {
        var run = MakeRun();
        var payouts = new List<CompensationPayout>
        {
            MakePayout(0m, "EUR"),
            MakePayout(0m, "EUR"),
        };

        run.UpdateRollUps(payouts);

        run.PayeeCount.Should().Be(2);
        run.PaidPayeeCount.Should().Be(0);
        run.ZeroPayoutCount.Should().Be(2);
        run.TotalAmounts.Should().BeEmpty();
    }

    [Fact]
    public void UpdateRollUps_EmptyList_AllZero()
    {
        var run = MakeRun();
        run.UpdateRollUps([]);

        run.PayeeCount.Should().Be(0);
        run.PaidPayeeCount.Should().Be(0);
        run.ZeroPayoutCount.Should().Be(0);
        run.TotalAmounts.Should().BeEmpty();
    }

    // Anti-Cartesian: same currency repeated across multiple payouts accumulates correctly.
    [Fact]
    public void UpdateRollUps_SameCurrencyMultiplePayouts_SumsWithoutDoubling()
    {
        var run = MakeRun();
        var payouts = Enumerable.Range(1, 5)
            .Select(i => MakePayout(100m * i, "EUR"))
            .ToList<CompensationPayout>();

        run.UpdateRollUps(payouts);

        // 100 + 200 + 300 + 400 + 500 = 1500, NOT 1500 * N (Cartesian product guard)
        run.TotalAmounts["EUR"].Should().Be(1500m);
        run.PayeeCount.Should().Be(5);
        run.PaidPayeeCount.Should().Be(5);
    }
}
