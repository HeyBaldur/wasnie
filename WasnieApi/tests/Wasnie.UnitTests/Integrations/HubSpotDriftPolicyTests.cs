using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Integrations.Crm;
using Wasnie.Application.Integrations.Crm.Drift;
using Wasnie.Application.Integrations.HubSpot;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Integrations.Crm;
using Wasnie.Infrastructure.Persistence;
using Wasnie.Infrastructure.Services.Crm;
using Wasnie.UnitTests.TestDoubles;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.UnitTests.Integrations;

/// <summary>
/// WI-HUBSPOT-DRIFT money-path tests: when a closed-won deal's amount/close-date CHANGES in HubSpot after
/// it was already imported, re-importing must reconcile it — never silently leave the transaction stale.
///   • Pending  → auto-void the old + re-create with the new values (Opción B). Payee preserved.
///   • Calculated/Paid → NEVER touched (Rule 10, anti-double-pay). Recorded as a drift alert for review.
/// Exercised end-to-end through the import handler with the REAL <see cref="CrmDriftPolicy"/> over an
/// in-memory DB and a mocked CRM boundary.
/// </summary>
public sealed class HubSpotDriftPolicyTests
{
    private const string Source = "HubSpot";
    private static readonly DateTime Now = new(2026, 6, 23, 10, 0, 0, DateTimeKind.Utc);

    private sealed class Harness
    {
        public required ApplicationDbContext Db { get; init; }
        public required ImportHubSpotDealsHandler Handler { get; init; }
        public required ICrmDealSource DealSource { get; init; }
        public required CrmDriftPolicy DriftPolicy { get; init; }
        public required FakeGuidGenerator Guid { get; init; }
        public required Guid TenantId { get; init; }
    }

    private static Harness BuildHarness(string dbName, Guid tenantId)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        var clock = new FakeClock(Now);
        var guid = new FakeGuidGenerator();

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("user-1");
        currentUser.Email.Returns("admin@acme.test");

        var authz = Substitute.For<IAuthorizationService>();

        var dealSource = Substitute.For<ICrmDealSource>();
        dealSource.SourceName.Returns(Source);

        var resolver = new CrmOwnerResolver(db, clock, guid, currentUser);
        var createGuard = new Wasnie.Application.Compensation.Common.TransactionCreateGuard(db);
        var driftPolicy = new CrmDriftPolicy(db, guid);
        var reconciler = new Wasnie.Application.Integrations.Crm.CrmDealReconciler(
            db, guid, resolver, createGuard, driftPolicy);

        var handler = new ImportHubSpotDealsHandler(
            tenantCtx, currentUser, clock, authz, dealSource, reconciler);

        return new Harness
        {
            Db = db, Handler = handler, DealSource = dealSource, DriftPolicy = driftPolicy,
            Guid = guid, TenantId = tenantId,
        };
    }

    private static void SetupSource(
        ICrmDealSource source, Guid tenantId,
        IReadOnlyList<CrmDeal> deals, IReadOnlyList<CrmOwner> owners, string? currency = "USD")
    {
        source.GetClosedWonDealsAsync(tenantId, Arg.Any<CancellationToken>()).Returns(deals);
        source.GetOwnersAsync(tenantId, Arg.Any<CancellationToken>()).Returns(owners);
        source.GetDefaultCurrencyAsync(tenantId, Arg.Any<CancellationToken>()).Returns(currency);
    }

    private static Payee SeedPayee(ApplicationDbContext db, Guid tenantId, string code, string? email)
    {
        var payee = Payee.Create(
            tenantId, $"Payee {code}", code, email, null, "seed", Guid.NewGuid(), new DateTimeOffset(Now));
        db.Payees.Add(payee);
        db.SaveChanges();
        return payee;
    }

    private static CrmDeal Deal(string id, decimal? amount, string? currency, DateOnly? closeDate, string? ownerId = "O1") =>
        new(id, "Deal", amount, currency, closeDate, ownerId);

    private static readonly CrmOwner[] OwnerAlice =
        { new("O1", "alice@example.com", "Alice", "A", false) };

    // ── No drift: unchanged deal keeps idempotency intact ─────────────────────────────────────────

    [Fact]
    public async Task Re_importing_an_unchanged_deal_does_not_drift_and_is_just_skipped()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Re_importing_an_unchanged_deal_does_not_drift_and_is_just_skipped), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");
        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 5000m, "USD", new DateOnly(2026, 6, 1)) }, OwnerAlice);

        (await h.Handler.Handle(new ImportHubSpotDealsCommand(), default)).Value!.Created.Should().Be(1);
        var second = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        second.Value!.DriftAutoResolved.Should().Be(0);
        second.Value.DriftAlertsRaised.Should().Be(0);
        second.Value.SkippedAlreadyImported.Should().Be(1);
        (await h.Db.CompensationTransactions.CountAsync()).Should().Be(1);
        (await h.Db.CrmDriftAlerts.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Tiny_rounding_difference_is_not_treated_as_drift()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Tiny_rounding_difference_is_not_treated_as_drift), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");
        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 5000m, "USD", new DateOnly(2026, 6, 1)) }, OwnerAlice);
        await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        // 5000.00004 rounds to 5000.0000 (Money is 4-dp banker's rounding) → must NOT look like a change.
        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 5000.00004m, "USD", new DateOnly(2026, 6, 1)) }, OwnerAlice);
        var second = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        second.Value!.DriftAutoResolved.Should().Be(0);
        second.Value.DriftAlertsRaised.Should().Be(0);
        second.Value.SkippedAlreadyImported.Should().Be(1);
        (await h.Db.CompensationTransactions.CountAsync()).Should().Be(1);
    }

    // ── Pending drift: auto-void + recreate ───────────────────────────────────────────────────────

    [Fact]
    public async Task Amount_change_on_a_pending_transaction_auto_voids_and_recreates_with_the_new_amount()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Amount_change_on_a_pending_transaction_auto_voids_and_recreates_with_the_new_amount), tenantId);
        var payee = SeedPayee(h.Db, tenantId, "E1", "alice@example.com");
        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 5000m, "USD", new DateOnly(2026, 6, 1)) }, OwnerAlice);
        await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        // GetYourGuide-style change: amount edited in HubSpot.
        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 72409.39m, "USD", new DateOnly(2026, 6, 1)) }, OwnerAlice);
        var second = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        second.Value!.DriftAutoResolved.Should().Be(1);
        second.Value.DriftAlertsRaised.Should().Be(0);

        var all = await h.Db.CompensationTransactions.OrderBy(t => t.Status).ToListAsync();
        all.Should().HaveCount(2);

        var cancelled = all.Single(t => t.Status == CompensationTransactionStatus.Cancelled);
        cancelled.Amount.Amount.Should().Be(5000m);
        cancelled.CancelledReason.Should().Contain("Auto-voided").And.Contain("72409.39");

        var active = all.Single(t => t.Status == CompensationTransactionStatus.Pending);
        active.Amount.Amount.Should().Be(72409.39m);
        active.Amount.Currency.Should().Be("USD");
        active.ExternalId.Should().Be("101");
        active.ReferenceNumber.Should().Be("HUBSPOT-101");
        active.PayeeId.Should().Be(payee.Id);          // payee match preserved (reused mapping)
        active.TransactionDate.Should().Be(new DateOnly(2026, 6, 1));

        (await h.Db.CrmDriftAlerts.AnyAsync()).Should().BeFalse();   // Pending fixes itself — no alert
        (await h.Db.AuditLogs.CountAsync(a => a.Action == "CRM_DRIFT_AUTO_RESOLVED")).Should().Be(2);
    }

    [Fact]
    public async Task Close_date_change_on_a_pending_transaction_auto_voids_and_recreates_with_the_new_date()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Close_date_change_on_a_pending_transaction_auto_voids_and_recreates_with_the_new_date), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");
        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 5000m, "USD", new DateOnly(2026, 6, 1)) }, OwnerAlice);
        await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 5000m, "USD", new DateOnly(2026, 7, 15)) }, OwnerAlice);
        var second = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        second.Value!.DriftAutoResolved.Should().Be(1);
        var active = await h.Db.CompensationTransactions
            .SingleAsync(t => t.Status == CompensationTransactionStatus.Pending);
        active.TransactionDate.Should().Be(new DateOnly(2026, 7, 15));
        active.Amount.Amount.Should().Be(5000m);
    }

    // ── Calculated / Paid drift: never touched, only alerted ──────────────────────────────────────

    [Fact]
    public async Task Amount_change_on_a_calculated_transaction_is_not_touched_and_raises_an_alert()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Amount_change_on_a_calculated_transaction_is_not_touched_and_raises_an_alert), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");
        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 5000m, "USD", new DateOnly(2026, 6, 1)) }, OwnerAlice);
        await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        // Engine calculated a commission on it — now it is immutable.
        var tx = await h.Db.CompensationTransactions.SingleAsync();
        tx.MarkCalculated(1, Money.Of(250m, "USD"), "engine", new DateTimeOffset(Now), Guid.NewGuid());
        await h.Db.SaveChangesAsync();

        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 9000m, "USD", new DateOnly(2026, 6, 1)) }, OwnerAlice);
        var second = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        second.Value!.DriftAutoResolved.Should().Be(0);
        second.Value.DriftAlertsRaised.Should().Be(1);

        // Untouched.
        var after = await h.Db.CompensationTransactions.SingleAsync();
        after.Status.Should().Be(CompensationTransactionStatus.Calculated);
        after.Amount.Amount.Should().Be(5000m);

        var alert = await h.Db.CrmDriftAlerts.SingleAsync();
        alert.TransactionId.Should().Be(tx.Id);
        alert.ExternalDealId.Should().Be("101");
        alert.TransactionStatus.Should().Be(CompensationTransactionStatus.Calculated);
        alert.AmountChanged.Should().BeTrue();
        alert.OldAmount.Should().Be(5000m);
        alert.NewAmount.Should().Be(9000m);
        alert.DateChanged.Should().BeFalse();
        alert.ResolvedAt.Should().BeNull();
        (await h.Db.AuditLogs.CountAsync(a => a.Action == "CRM_DRIFT_DETECTED")).Should().Be(1);
    }

    [Fact]
    public async Task Amount_change_on_a_paid_transaction_is_not_touched_and_raises_an_alert()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Amount_change_on_a_paid_transaction_is_not_touched_and_raises_an_alert), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");
        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 5000m, "USD", new DateOnly(2026, 6, 1)) }, OwnerAlice);
        await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        var tx = await h.Db.CompensationTransactions.SingleAsync();
        tx.MarkCalculated(1, Money.Of(250m, "USD"), "engine", new DateTimeOffset(Now), Guid.NewGuid());
        tx.MarkPaid("payrun", new DateTimeOffset(Now), Guid.NewGuid());
        await h.Db.SaveChangesAsync();

        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 1m, "USD", new DateOnly(2026, 6, 1)) }, OwnerAlice);
        var second = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        second.Value!.DriftAlertsRaised.Should().Be(1);
        var after = await h.Db.CompensationTransactions.SingleAsync();
        after.Status.Should().Be(CompensationTransactionStatus.Paid);   // anti-double-pay intact
        after.Amount.Amount.Should().Be(5000m);
        (await h.Db.CrmDriftAlerts.SingleAsync()).TransactionStatus.Should().Be(CompensationTransactionStatus.Paid);
    }

    [Fact]
    public async Task Re_importing_a_still_drifted_calculated_deal_refreshes_the_alert_instead_of_duplicating()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Re_importing_a_still_drifted_calculated_deal_refreshes_the_alert_instead_of_duplicating), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");
        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 5000m, "USD", new DateOnly(2026, 6, 1)) }, OwnerAlice);
        await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);
        var tx = await h.Db.CompensationTransactions.SingleAsync();
        tx.MarkCalculated(1, Money.Of(250m, "USD"), "engine", new DateTimeOffset(Now), Guid.NewGuid());
        await h.Db.SaveChangesAsync();

        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 9000m, "USD", new DateOnly(2026, 6, 1)) }, OwnerAlice);
        await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);
        // The deal changes AGAIN before the next import.
        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 12000m, "USD", new DateOnly(2026, 6, 1)) }, OwnerAlice);
        await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        var alert = await h.Db.CrmDriftAlerts.SingleAsync();   // still exactly one
        alert.NewAmount.Should().Be(12000m);                    // refreshed to the latest CRM figure
    }

    // ── Race: a Pending that became Calculated between detection and action is NOT auto-voided ─────

    [Fact]
    public async Task Pending_that_turned_calculated_before_the_policy_runs_degrades_to_an_alert()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Pending_that_turned_calculated_before_the_policy_runs_degrades_to_an_alert), tenantId);
        var payee = SeedPayee(h.Db, tenantId, "E1", "alice@example.com");
        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 5000m, "USD", new DateOnly(2026, 6, 1)) }, OwnerAlice);
        await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        var tx = await h.Db.CompensationTransactions.SingleAsync();

        // Build the candidate while the tx is still Pending (this is what the importer captured)...
        var candidate = new CrmDriftCandidate(
            new CrmDriftIncoming("101", Money.Of(9000m, "USD"), new DateOnly(2026, 6, 1)), tx);

        // ...but the engine calculates it in the gap before the policy acts.
        tx.MarkCalculated(1, Money.Of(250m, "USD"), "engine", new DateTimeOffset(Now), Guid.NewGuid());
        await h.Db.SaveChangesAsync();

        var result = await h.DriftPolicy.ReconcileAsync(
            TransactionSource.CrmSync, Source, new[] { candidate }, new DateTimeOffset(Now), "user-1", "admin@acme.test", default);

        result.AutoResolvedCount.Should().Be(0);
        result.AlertedCount.Should().Be(1);
        (await h.Db.CompensationTransactions.SingleAsync()).Status
            .Should().Be(CompensationTransactionStatus.Calculated);   // never voided
        (await h.Db.CrmDriftAlerts.CountAsync()).Should().Be(1);
    }

    // ── Tenant isolation ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Drift_is_tenant_isolated()
    {
        const string dbName = nameof(Drift_is_tenant_isolated);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var a = BuildHarness(dbName, tenantA);
        SeedPayee(a.Db, tenantA, "A1", "alice@example.com");
        SetupSource(a.DealSource, tenantA, new[] { Deal("777", 100m, "USD", new DateOnly(2026, 6, 1)) }, OwnerAlice);
        await a.Handler.Handle(new ImportHubSpotDealsCommand(), default);
        var txA = await a.Db.CompensationTransactions.SingleAsync();
        txA.MarkCalculated(1, Money.Of(5m, "USD"), "engine", new DateTimeOffset(Now), Guid.NewGuid());
        await a.Db.SaveChangesAsync();

        var b = BuildHarness(dbName, tenantB);
        SeedPayee(b.Db, tenantB, "B1", "alice@example.com");
        // Same deal id 777 changed, but it belongs to tenant B (a separate, still-Pending transaction).
        SetupSource(b.DealSource, tenantB, new[] { Deal("777", 100m, "USD", new DateOnly(2026, 6, 1)) }, OwnerAlice);
        await b.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        SetupSource(b.DealSource, tenantB, new[] { Deal("777", 999m, "USD", new DateOnly(2026, 6, 1)) }, OwnerAlice);
        var bSecond = await b.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        // Tenant B's Pending drift auto-resolved; tenant A untouched and has no alert from B's activity.
        bSecond.Value!.DriftAutoResolved.Should().Be(1);
        (await a.Db.CompensationTransactions.SingleAsync()).Status.Should().Be(CompensationTransactionStatus.Calculated);
        (await a.Db.CrmDriftAlerts.AnyAsync()).Should().BeFalse();
        (await b.Db.CrmDriftAlerts.AnyAsync()).Should().BeFalse();   // B's was Pending → no alert
    }
}
