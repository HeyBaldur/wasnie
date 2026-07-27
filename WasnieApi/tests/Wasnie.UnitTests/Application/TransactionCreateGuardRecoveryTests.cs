using FluentAssertions;
using NSubstitute;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Common;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The create-guard's lost→won recovery decision. A CRM transaction cancelled BECAUSE its deal was lost —
/// whose commission was never paid — may be re-created when the deal returns (RecreateAfterDealLost). Any
/// other cancelled-with-credits row, or one whose credit was ever paid, stays BLOCKED (anti-double-pay).
/// </summary>
public sealed class TransactionCreateGuardRecoveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private const string ExternalId = "900-1";
    private const string Reference = "HUBSPOT-900-1";

    private static (ApplicationDbContext Db, TransactionCreateGuard Guard, Guid TenantId) Build(string dbName)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());
        return (db, new TransactionCreateGuard(db), tenantId);
    }

    /// <summary>Seeds a CrmSync tx that reached Calculated with one credit, then was Cancelled with the given
    /// reason. <paramref name="consumed"/> marks the credit as paid (ConsumedAt set).</summary>
    private static void SeedCancelledWithCredit(ApplicationDbContext db, Guid tenantId, string reason, bool consumed)
    {
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();

        var tx = CompensationTransaction.Ingest(
            tenantId: tenantId, referenceNumber: Reference, payeeId: payeeId,
            amount: Money.Of(1000m, "EUR"), transactionDate: new DateOnly(2026, 6, 1),
            source: TransactionSource.CrmSync, ingestedBy: "sync", id: Guid.NewGuid(), now: Now,
            eventId: Guid.NewGuid(), externalId: ExternalId);

        var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission", RateTable.Flat(0.10m), Trigger.Always(), Now);
        var credit = Credit.Allocate(tenantId, tx.Id, payeeId, planId, ruleId, snapshot,
            Money.Of(1000m, "EUR"), Money.Of(100m, "EUR"), Percentage.FromPercent(100), CreditRole.Primary,
            "sync", Guid.NewGuid(), Now, Guid.NewGuid());
        tx.MarkCalculated(1, Money.Of(100m, "EUR"), "sync", Now, Guid.NewGuid());
        if (consumed)
            credit.Consume(Guid.NewGuid(), Now, Guid.NewGuid());
        else
            credit.Supersede("deal lost", Now, Guid.NewGuid());
        // RevertForLostDeal just flips Calculated→Cancelled with the reason (domain-level; guards live in the handler).
        tx.RevertForLostDeal(reason, "admin", Now, Guid.NewGuid());

        db.CompensationTransactions.Add(tx);
        db.Credits.Add(credit);
        db.SaveChanges();
    }

    private static async Task<TransactionCreateDecision> DecideAsync(
        (ApplicationDbContext Db, TransactionCreateGuard Guard, Guid TenantId) h, TransactionSource source)
    {
        var c = await h.Guard.ClassifyAsync(source, [Reference], [ExternalId]);
        return c.Decide(Reference, ExternalId);
    }

    [Fact]
    public async Task Deal_lost_cancellation_unpaid_is_recoverable()
    {
        var h = Build(nameof(Deal_lost_cancellation_unpaid_is_recoverable));
        SeedCancelledWithCredit(h.Db, h.TenantId, "Deal lost in CRM (deal 900).", consumed: false);

        (await DecideAsync(h, TransactionSource.CrmSync))
            .Should().Be(TransactionCreateDecision.RecreateAfterDealLost);
    }

    [Fact]
    public async Task Cancellation_for_another_reason_stays_blocked()
    {
        var h = Build(nameof(Cancellation_for_another_reason_stays_blocked));
        SeedCancelledWithCredit(h.Db, h.TenantId, "Manually voided by admin.", consumed: false);

        (await DecideAsync(h, TransactionSource.CrmSync))
            .Should().Be(TransactionCreateDecision.BlockedVoidHadCredits);
    }

    [Fact]
    public async Task Deal_lost_cancellation_with_a_PAID_credit_stays_blocked()
    {
        var h = Build(nameof(Deal_lost_cancellation_with_a_PAID_credit_stays_blocked));
        // Anti-double-pay: even with the deal-lost reason, a consumed (paid) credit must never be re-created.
        SeedCancelledWithCredit(h.Db, h.TenantId, "Deal lost in CRM (deal 900).", consumed: true);

        (await DecideAsync(h, TransactionSource.CrmSync))
            .Should().Be(TransactionCreateDecision.BlockedVoidHadCredits);
    }

    [Fact]
    public async Task Non_crm_source_does_not_open_recovery()
    {
        var h = Build(nameof(Non_crm_source_does_not_open_recovery));
        SeedCancelledWithCredit(h.Db, h.TenantId, "Deal lost in CRM (deal 900).", consumed: false);

        // Manual/Excel keep the strict block — recovery is a CRM-sync-only path.
        (await DecideAsync(h, TransactionSource.Manual))
            .Should().Be(TransactionCreateDecision.BlockedVoidHadCredits);
    }

    [Fact]
    public async Task An_active_recreated_row_wins_idempotently()
    {
        var h = Build(nameof(An_active_recreated_row_wins_idempotently));
        SeedCancelledWithCredit(h.Db, h.TenantId, "Deal lost in CRM (deal 900).", consumed: false);
        // Simulate the recovery having already re-created a fresh active tx with the same keys.
        h.Db.CompensationTransactions.Add(CompensationTransaction.Ingest(
            tenantId: h.TenantId, referenceNumber: Reference, payeeId: Guid.NewGuid(),
            amount: Money.Of(1000m, "EUR"), transactionDate: new DateOnly(2026, 6, 1),
            source: TransactionSource.CrmSync, ingestedBy: "sync", id: Guid.NewGuid(), now: Now,
            eventId: Guid.NewGuid(), externalId: ExternalId));
        await h.Db.SaveChangesAsync();

        (await DecideAsync(h, TransactionSource.CrmSync))
            .Should().Be(TransactionCreateDecision.SkipActiveDuplicate);
    }
}
