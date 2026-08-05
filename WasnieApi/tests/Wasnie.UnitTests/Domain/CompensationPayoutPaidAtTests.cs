using FluentAssertions;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.UnitTests.Domain;

/// <summary>
/// PaidAt is the timestamp of a real financial event: the moment money left. Cash-flow reporting
/// attributes euros to months using it, so a PaidAt that drifts silently moves money between reporting
/// periods — including periods already closed and reported.
///
/// INVARIANT under test: PaidAt has a value if and only if Status == Paid.
/// </summary>
public sealed class CompensationPayoutPaidAtTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PayeeId = Guid.NewGuid();

    private static readonly DateTimeOffset Calculated = At(2026, 7, 1);
    private static readonly DateTimeOffset Approved = At(2026, 7, 10);
    private static readonly DateTimeOffset PaymentDay = At(2026, 7, 15);

    private static DateTimeOffset At(int y, int m, int d) =>
        new(new DateTime(y, m, d, 9, 30, 0, DateTimeKind.Utc), TimeSpan.Zero);

    private static CompensationPayout NewPayout(decimal commission = 1_000m)
    {
        var spec = new PayoutLineSpec(
            CreditId: Guid.NewGuid(),
            RuleId: Guid.NewGuid(),
            RuleName: "Base",
            BaseAmount: Money.Of(commission * 10m, "EUR"),
            CommissionAmount: Money.Of(commission, "EUR"),
            AppliedModifiers: []);

        return CompensationPayout.Calculate(
            TenantId, PayeeId, Guid.NewGuid(),
            PayeeReference.Snapshot(PayeeId, "Test Payee", "EMP-001"),
            DateRange.Of(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)),
            [spec], "EUR", "test", Guid.NewGuid(), Calculated, Guid.NewGuid(),
            () => Guid.NewGuid());
    }

    private static CompensationPayout PaidPayout()
    {
        var p = NewPayout();
        p.Approve("approver", Approved, Guid.NewGuid());
        p.MarkPaid("payer", PaymentDay);
        return p;
    }

    // ── stamping ──────────────────────────────────────────────────────────────

    [Fact]
    public void NewlyCalculatedPayout_HasNoPaymentDate()
    {
        var payout = NewPayout();

        payout.Status.Should().Be(CompensationPayoutStatus.Calculated);
        payout.PaidAt.Should().BeNull("nothing has been paid yet");
    }

    [Fact]
    public void Approving_DoesNotSetAPaymentDate()
    {
        var payout = NewPayout();

        payout.Approve("approver", Approved, Guid.NewGuid());

        payout.Status.Should().Be(CompensationPayoutStatus.Approved);
        payout.PaidAt.Should().BeNull("approval authorises a payment, it does not make one");
    }

    [Fact]
    public void MarkPaid_StampsTheMomentOfPayment()
    {
        var payout = NewPayout();
        payout.Approve("approver", Approved, Guid.NewGuid());

        payout.MarkPaid("payer", PaymentDay);

        payout.Status.Should().Be(CompensationPayoutStatus.Paid);
        payout.PaidAt.Should().Be(PaymentDay);
    }

    [Fact]
    public void MarkPaid_StampsThePaymentInstant_NotTheEndOfTheCompensationPeriod()
    {
        // The payout covers July but is paid in August. PaidAt must follow the payment, because
        // reporting it against the period end is precisely the defect this column was added to fix.
        var payout = NewPayout();
        payout.Approve("approver", Approved, Guid.NewGuid());
        var paidInAugust = At(2026, 8, 3);

        payout.MarkPaid("payer", paidInAugust);

        payout.PaidAt.Should().Be(paidInAugust);
        payout.Period.End.Should().Be(new DateOnly(2026, 7, 31));
        payout.PaidAt!.Value.Date.Should().NotBe(payout.Period.End.ToDateTime(TimeOnly.MinValue));
    }

    // ── immutability while Paid ───────────────────────────────────────────────

    [Fact]
    public void PaidPayout_CannotBeMarkedPaidAgain_SoItsPaymentDateCannotMove()
    {
        var payout = PaidPayout();

        var act = () => payout.MarkPaid("someone else", At(2026, 9, 1));

        act.Should().Throw<DomainException>();
        payout.PaidAt.Should().Be(PaymentDay, "the original payment date survives the attempt");
    }

    [Fact]
    public void PaidPayout_CannotBeApproved_SoApprovalCannotClearThePaymentDate()
    {
        var payout = PaidPayout();

        var act = () => payout.Approve("approver", At(2026, 9, 1), Guid.NewGuid());

        act.Should().Throw<DomainException>();
        payout.PaidAt.Should().Be(PaymentDay);
    }

    [Fact]
    public void PaidPayout_CannotBeDisputed_SoDisputeCannotStrandThePaymentDate()
    {
        var payout = PaidPayout();

        var act = () => payout.Dispute("someone", At(2026, 9, 1));

        act.Should().Throw<DomainException>();
        payout.PaidAt.Should().Be(PaymentDay);
        payout.Status.Should().Be(CompensationPayoutStatus.Paid);
    }

    [Fact]
    public void AssigningToAPayRun_DoesNotTouchThePaymentDate()
    {
        var payout = PaidPayout();

        payout.AssignToRun(Guid.NewGuid());

        payout.PaidAt.Should().Be(PaymentDay);
    }

    // ── the revert path ───────────────────────────────────────────────────────

    [Fact]
    public void RevertingAPayment_ClearsThePaymentDate()
    {
        var payout = PaidPayout();

        payout.RevertPaidToApproved("reverter", At(2026, 7, 20));

        payout.Status.Should().Be(CompensationPayoutStatus.Approved);
        payout.PaidAt.Should().BeNull(
            "the payment was undone — credits unconsumed and transactions returned to Calculated — so " +
            "claiming money left on that day would double count it against the eventual re-payment");
    }

    [Fact]
    public void PayingAgainAfterARevert_RecordsTheNewPaymentDate_NotTheOriginal()
    {
        var payout = PaidPayout();
        payout.RevertPaidToApproved("reverter", At(2026, 7, 20));
        var secondPayment = At(2026, 8, 5);

        payout.MarkPaid("payer", secondPayment);

        payout.PaidAt.Should().Be(secondPayment,
            "the money left the account on the second date; the first attempt was taken back");
        payout.PaidAt.Should().NotBe(PaymentDay);
    }

    // ── the invariant, stated directly ────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PaymentDateIsPresentExactlyWhenStatusIsPaid(bool revertAfterPaying)
    {
        var payout = NewPayout();
        payout.Approve("approver", Approved, Guid.NewGuid());
        payout.MarkPaid("payer", PaymentDay);

        if (revertAfterPaying)
            payout.RevertPaidToApproved("reverter", At(2026, 7, 20));

        var isPaid = payout.Status == CompensationPayoutStatus.Paid;
        payout.PaidAt.HasValue.Should().Be(isPaid);
    }

    [Fact]
    public void RevertingToCalculatedFromApproved_LeavesNoPaymentDateBehind()
    {
        var payout = NewPayout();
        payout.Approve("approver", Approved, Guid.NewGuid());

        payout.RevertToCalculated("reverter", At(2026, 7, 12));

        payout.Status.Should().Be(CompensationPayoutStatus.Calculated);
        payout.PaidAt.Should().BeNull();
    }
}
