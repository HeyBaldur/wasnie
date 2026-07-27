using FluentAssertions;
using NSubstitute;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Integrations.Crm;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Integrations;

/// <summary>
/// Reverse reconciliation: a CrmSync commission (Calculated/Paid) whose deal is no longer closed-won is
/// flagged with a DealLostAlert; a still-won deal is not; an absent deal (ambiguous) is left alone.
/// </summary>
public sealed class DealLostReconcilerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Source = "HubSpot";

    private sealed record Harness(ApplicationDbContext Db, DealLostReconciler Reconciler, ICrmDealSource Source, Guid TenantId);

    private static Harness Build(string dbName)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<Wasnie.Application.Common.Interfaces.ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        var source = Substitute.For<ICrmDealSource>();
        source.SourceName.Returns(Source);

        return new Harness(db, new DealLostReconciler(db, source, new FakeGuidGenerator()), source, tenantId);
    }

    /// <summary>Seeds a Calculated CrmSync transaction for line item {dealId}-{lineId} with one live credit.</summary>
    private static (Guid TxId, string ExternalId) SeedCalculated(
        ApplicationDbContext db, Guid tenantId, string dealId, string lineId, decimal amount)
    {
        var externalId = $"{dealId}-{lineId}";
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();

        var tx = CompensationTransaction.Ingest(
            tenantId: tenantId, referenceNumber: $"HUBSPOT-{externalId}", payeeId: payeeId,
            amount: Money.Of(amount, "EUR"), transactionDate: new DateOnly(2026, 6, 1),
            source: TransactionSource.CrmSync, ingestedBy: "sync", id: Guid.NewGuid(), now: Now,
            eventId: Guid.NewGuid(), externalId: externalId);

        var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission", RateTable.Flat(0.10m), Trigger.Always(), Now);
        var commission = Money.Of(amount * 0.10m, "EUR");
        var credit = Credit.Allocate(tenantId, tx.Id, payeeId, planId, ruleId, snapshot,
            Money.Of(amount, "EUR"), commission, Percentage.FromPercent(100), CreditRole.Primary,
            "sync", Guid.NewGuid(), Now, Guid.NewGuid());
        tx.MarkCalculated(1, commission, "sync", Now, Guid.NewGuid());

        db.CompensationTransactions.Add(tx);
        db.Credits.Add(credit);
        db.SaveChanges();
        return (tx.Id, externalId);
    }

    private void SetStatuses(Harness h, params (string DealId, bool IsWon)[] statuses) =>
        h.Source.GetDealStatusesByIdsAsync(h.TenantId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(statuses.Select(s => new CrmDealStatus(s.DealId, s.IsWon)).ToList());

    [Fact]
    public async Task Deal_no_longer_closed_won_raises_an_alert()
    {
        var h = Build(nameof(Deal_no_longer_closed_won_raises_an_alert));
        SeedCalculated(h.Db, h.TenantId, "1000", "77", 1000m);
        SetStatuses(h, ("1000", false));

        var count = await h.Reconciler.ReconcileAsync(h.TenantId, Source, "sync", "sync", Now, default);

        count.Should().Be(1);
        var alert = await h.Db.DealLostAlerts.SingleAsync();
        alert.ExternalDealId.Should().Be("1000");
        alert.TransactionStatus.Should().Be(CompensationTransactionStatus.Calculated);
        alert.CommissionAmount.Should().Be(100m); // 1000 * 0.10
        alert.CommissionCurrency.Should().Be("EUR");
        alert.ResolvedAt.Should().BeNull();
    }

    [Fact]
    public async Task Deal_still_closed_won_raises_no_alert()
    {
        var h = Build(nameof(Deal_still_closed_won_raises_no_alert));
        SeedCalculated(h.Db, h.TenantId, "1000", "77", 1000m);
        SetStatuses(h, ("1000", true));

        var count = await h.Reconciler.ReconcileAsync(h.TenantId, Source, "sync", "sync", Now, default);

        count.Should().Be(0);
        (await h.Db.DealLostAlerts.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Absent_deal_is_treated_conservatively_no_alert()
    {
        var h = Build(nameof(Absent_deal_is_treated_conservatively_no_alert));
        SeedCalculated(h.Db, h.TenantId, "1000", "77", 1000m);
        SetStatuses(h); // CRM returned nothing for the id (deleted/archived/no access)

        var count = await h.Reconciler.ReconcileAsync(h.TenantId, Source, "sync", "sync", Now, default);

        count.Should().Be(0);
        (await h.Db.DealLostAlerts.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Running_twice_keeps_a_single_unresolved_alert()
    {
        var h = Build(nameof(Running_twice_keeps_a_single_unresolved_alert));
        SeedCalculated(h.Db, h.TenantId, "1000", "77", 1000m);
        SetStatuses(h, ("1000", false));

        await h.Reconciler.ReconcileAsync(h.TenantId, Source, "sync", "sync", Now, default);
        await h.Reconciler.ReconcileAsync(h.TenantId, Source, "sync", "sync", Now.AddHours(1), default);

        (await h.Db.DealLostAlerts.CountAsync(a => a.ResolvedAt == null)).Should().Be(1);
    }
}
