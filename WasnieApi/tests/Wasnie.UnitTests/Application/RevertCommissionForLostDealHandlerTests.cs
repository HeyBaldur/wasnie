using FluentAssertions;
using NSubstitute;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Transactions;
using Wasnie.Application.Compensation.Handlers.Transactions;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Integrations.Crm;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// Reverting a Calculated commission for a lost deal: supersedes credits (non-destructive) + cancels the
/// transaction + resolves the alert. Money guards: Paid is refused, no-alert is refused, and a credit
/// already committed to an Approved payout is refused.
/// </summary>
public sealed class RevertCommissionForLostDealHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed record Seed(Guid TxId, Guid CreditId, Guid PayeeId, Guid PlanId);

    private sealed record Harness(ApplicationDbContext Db, RevertCommissionForLostDealHandler Handler, Guid TenantId);

    private static Harness Build(string dbName)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("admin");

        var handler = new RevertCommissionForLostDealHandler(
            db, currentUser, new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
            Substitute.For<IAuthorizationService>());

        return new Harness(db, handler, tenantId);
    }

    /// <summary>Seeds a Calculated (or Paid) CrmSync tx + one live credit + an open DealLostAlert.</summary>
    private static Seed SeedLostDeal(ApplicationDbContext db, Guid tenantId, bool paid = false)
    {
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        const string externalId = "5000-1";

        var tx = CompensationTransaction.Ingest(
            tenantId: tenantId, referenceNumber: $"HUBSPOT-{externalId}", payeeId: payeeId,
            amount: Money.Of(1000m, "EUR"), transactionDate: new DateOnly(2026, 6, 1),
            source: TransactionSource.CrmSync, ingestedBy: "sync", id: Guid.NewGuid(), now: Now,
            eventId: Guid.NewGuid(), externalId: externalId);

        var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission", RateTable.Flat(0.10m), Trigger.Always(), Now);
        var commission = Money.Of(100m, "EUR");
        var credit = Credit.Allocate(tenantId, tx.Id, payeeId, planId, ruleId, snapshot,
            Money.Of(1000m, "EUR"), commission, Percentage.FromPercent(100), CreditRole.Primary,
            "sync", Guid.NewGuid(), Now, Guid.NewGuid());
        tx.MarkCalculated(1, commission, "sync", Now, Guid.NewGuid());
        if (paid)
            tx.MarkPaid("sync", Now, Guid.NewGuid());

        var alert = DealLostAlert.Create(
            Guid.NewGuid(), tenantId, "HubSpot", "5000", tx.Id, tx.ReferenceNumber,
            tx.Status, 100m, "EUR", Now, "sync");

        db.CompensationTransactions.Add(tx);
        db.Credits.Add(credit);
        db.DealLostAlerts.Add(alert);
        db.SaveChanges();
        return new Seed(tx.Id, credit.Id, payeeId, planId);
    }

    [Fact]
    public async Task Calculated_revert_supersedes_credit_cancels_tx_and_resolves_alert()
    {
        var h = Build(nameof(Calculated_revert_supersedes_credit_cancels_tx_and_resolves_alert));
        var s = SeedLostDeal(h.Db, h.TenantId);

        var result = await h.Handler.Handle(new RevertCommissionForLostDealCommand(s.TxId), default);

        result.IsSuccess.Should().BeTrue();
        (await h.Db.Credits.SingleAsync(c => c.Id == s.CreditId)).SupersededAt.Should().NotBeNull();
        (await h.Db.CompensationTransactions.SingleAsync(t => t.Id == s.TxId)).Status
            .Should().Be(CompensationTransactionStatus.Cancelled);
        (await h.Db.DealLostAlerts.SingleAsync()).ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Superseded_credit_drops_out_of_the_payout_query()
    {
        var h = Build(nameof(Superseded_credit_drops_out_of_the_payout_query));
        var s = SeedLostDeal(h.Db, h.TenantId);

        await h.Handler.Handle(new RevertCommissionForLostDealCommand(s.TxId), default);

        // Mirror CalculatePayoutsForPeriodHandler's credit filter: superseded credits are excluded.
        var payable = await h.Db.Credits
            .Where(c => c.SupersededAt == null && c.ConsumedAt == null && c.TransactionId == s.TxId)
            .AnyAsync();
        payable.Should().BeFalse();
    }

    [Fact]
    public async Task Paid_commission_is_refused_and_nothing_changes()
    {
        var h = Build(nameof(Paid_commission_is_refused_and_nothing_changes));
        var s = SeedLostDeal(h.Db, h.TenantId, paid: true);

        var result = await h.Handler.Handle(new RevertCommissionForLostDealCommand(s.TxId), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already paid");
        (await h.Db.Credits.SingleAsync(c => c.Id == s.CreditId)).SupersededAt.Should().BeNull();
        (await h.Db.CompensationTransactions.SingleAsync(t => t.Id == s.TxId)).Status
            .Should().Be(CompensationTransactionStatus.Paid);
        (await h.Db.DealLostAlerts.SingleAsync()).ResolvedAt.Should().BeNull();
    }

    [Fact]
    public async Task Transaction_without_an_open_alert_is_refused()
    {
        var h = Build(nameof(Transaction_without_an_open_alert_is_refused));
        var s = SeedLostDeal(h.Db, h.TenantId);
        // Resolve the alert so there is no open one.
        var alert = await h.Db.DealLostAlerts.SingleAsync();
        alert.Resolve("admin", Now);
        await h.Db.SaveChangesAsync();

        var result = await h.Handler.Handle(new RevertCommissionForLostDealCommand(s.TxId), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("no open lost-deal alert");
    }

    [Fact]
    public async Task Credit_committed_to_an_approved_payout_is_refused()
    {
        var h = Build(nameof(Credit_committed_to_an_approved_payout_is_refused));
        var s = SeedLostDeal(h.Db, h.TenantId);

        // Build an Approved payout whose single line references the credit.
        var credit = await h.Db.Credits.SingleAsync(c => c.Id == s.CreditId);
        var lineSpec = new PayoutLineSpec(credit.Id, Guid.NewGuid(), "Commission",
            credit.OriginalAmount, credit.CreditedAmount, []);
        var payout = CompensationPayout.Calculate(
            h.TenantId, s.PayeeId, s.PlanId, PayeeReference.Snapshot(s.PayeeId, "Payee", "E1"),
            DateRange.Of(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)), [lineSpec], "EUR",
            "admin", Guid.NewGuid(), Now, Guid.NewGuid(), Guid.NewGuid);
        payout.Approve("admin", Now, Guid.NewGuid());
        h.Db.CompensationPayouts.Add(payout);
        await h.Db.SaveChangesAsync();

        var result = await h.Handler.Handle(new RevertCommissionForLostDealCommand(s.TxId), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("approved or paid payout");
        (await h.Db.Credits.SingleAsync(c => c.Id == s.CreditId)).SupersededAt.Should().BeNull();
    }
}
