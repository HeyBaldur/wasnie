using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Integrations.Crm;
using Wasnie.Application.Integrations.Crm.Drift;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Integrations.HubSpot;
using Wasnie.Infrastructure.BackgroundJobs;
using Wasnie.Infrastructure.Identity;
using Wasnie.Infrastructure.Persistence;
using Wasnie.Infrastructure.Services.Crm;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Integrations;

/// <summary>
/// Phase-3 polling job (per-tenant). Verifies the incremental sync reuses the shared reconciler/drift
/// logic, advances the checkpoint only on success, and is safe/idempotent + tenant-isolated.
/// </summary>
public sealed class HubSpotTenantSyncJobTests
{
    private const string Source = "HubSpot";
    private static readonly DateTime Now = new(2026, 6, 23, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset ConnectedAt = new(new DateTime(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc));

    private sealed class Harness
    {
        public required ApplicationDbContext Db { get; init; }
        public required HubSpotTenantSyncJob Job { get; init; }
        public required ICrmDealSource DealSource { get; init; }
        public required Guid TenantId { get; init; }
    }

    private static Harness BuildHarness(string dbName, Guid tenantId)
    {
        var tenantCtx = new BackgroundJobTenantContext();
        tenantCtx.SetTenant(tenantId); // pre-set for seeding; the job re-sets the same value.

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        var clock = new FakeClock(Now);
        var guid = new FakeGuidGenerator();

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("system");

        var dealSource = Substitute.For<ICrmDealSource>();
        dealSource.SourceName.Returns(Source);

        var resolver = new CrmOwnerResolver(db, clock, guid, currentUser);
        var createGuard = new Wasnie.Application.Compensation.Common.TransactionCreateGuard(db);
        var driftPolicy = new CrmDriftPolicy(db, guid);
        var reconciler = new CrmDealReconciler(db, guid, resolver, createGuard, driftPolicy,
            new Wasnie.UnitTests.TestDoubles.FakeTransactionEnrichmentService());
        var dealLostReconciler = new Wasnie.Application.Integrations.Crm.DealLostReconciler(
            db, dealSource, guid, Substitute.For<MediatR.ISender>());

        var job = new HubSpotTenantSyncJob(
            tenantCtx, db, dealSource, reconciler, dealLostReconciler, clock, NullLogger<HubSpotTenantSyncJob>.Instance);

        return new Harness { Db = db, Job = job, DealSource = dealSource, TenantId = tenantId };
    }

    private static void SeedConnection(ApplicationDbContext db, Guid tenantId, HubSpotConnectionStatus status)
    {
        var c = HubSpotConnection.Create(
            Guid.NewGuid(), tenantId, 42, "enc-access", "enc-refresh",
            ConnectedAt.AddHours(1), "owner", ConnectedAt);
        if (status == HubSpotConnectionStatus.NeedsReconnect)
            c.MarkNeedsReconnect("token revoked", ConnectedAt.AddDays(1));
        else if (status == HubSpotConnectionStatus.Disconnected)
            c.Disconnect("user disconnected", "owner", ConnectedAt.AddDays(1));
        db.HubSpotConnections.Add(c);
        db.SaveChanges();
    }

    private static Payee SeedPayee(ApplicationDbContext db, Guid tenantId, string email)
    {
        var payee = Payee.Create(tenantId, "Alice", "E1", email, null, "seed", Guid.NewGuid(), new DateTimeOffset(Now));
        db.Payees.Add(payee);
        db.SaveChanges();
        return payee;
    }

    private static void SetupSource(
        ICrmDealSource source, Guid tenantId, IReadOnlyList<CrmDeal> deals, string? currency = "USD")
    {
        source.GetClosedWonDealsModifiedSinceAsync(tenantId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(deals);
        source.GetOwnersAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new[] { new CrmOwner("O1", "alice@example.com", "Alice", "A", false) });
        source.GetDefaultCurrencyAsync(tenantId, Arg.Any<CancellationToken>()).Returns(currency);
    }

    private static CrmDeal Deal(string id, decimal amount, DateOnly close) =>
        new(id, "Deal", amount, "USD", close, "O1");

    // ── New deal via polling → transaction created; checkpoint advances ───────────────────────────

    [Fact]
    public async Task New_closed_won_deal_via_polling_creates_a_transaction_and_advances_the_checkpoint()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(New_closed_won_deal_via_polling_creates_a_transaction_and_advances_the_checkpoint), tenantId);
        SeedConnection(h.Db, tenantId, HubSpotConnectionStatus.Connected);
        var payee = SeedPayee(h.Db, tenantId, "alice@example.com");
        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 5000m, new DateOnly(2026, 6, 1)) });

        await h.Job.SyncTenantAsync(tenantId, default);

        var tx = await h.Db.CompensationTransactions.SingleAsync();
        tx.ExternalId.Should().Be("101");
        tx.Amount.Amount.Should().Be(5000m);
        tx.PayeeId.Should().Be(payee.Id);
        tx.Status.Should().Be(CompensationTransactionStatus.Pending);

        // First run uses ConnectedAt as the floor.
        await h.DealSource.Received(1)
            .GetClosedWonDealsModifiedSinceAsync(tenantId, ConnectedAt, Arg.Any<CancellationToken>());

        var conn = await h.Db.HubSpotConnections.IgnoreQueryFilters().SingleAsync();
        conn.LastSyncedAt.Should().Be(new DateTimeOffset(Now));   // advanced to run start
        (await h.Db.AuditLogs.CountAsync(a => a.Action == "CRM_AUTO_SYNC_COMPLETED")).Should().Be(1);
    }

    [Fact]
    public async Task Second_run_polls_incrementally_from_the_saved_checkpoint_not_from_scratch()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Second_run_polls_incrementally_from_the_saved_checkpoint_not_from_scratch), tenantId);
        SeedConnection(h.Db, tenantId, HubSpotConnectionStatus.Connected);
        SeedPayee(h.Db, tenantId, "alice@example.com");
        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 5000m, new DateOnly(2026, 6, 1)) });

        await h.Job.SyncTenantAsync(tenantId, default);              // run 1 → checkpoint = Now
        SetupSource(h.DealSource, tenantId, Array.Empty<CrmDeal>()); // run 2 → nothing new
        await h.Job.SyncTenantAsync(tenantId, default);

        // The second run asks for deals modified since the checkpoint set by run 1 (== Now), NOT ConnectedAt.
        await h.DealSource.Received(1)
            .GetClosedWonDealsModifiedSinceAsync(tenantId, new DateTimeOffset(Now), Arg.Any<CancellationToken>());
    }

    // ── Drift via polling ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Changed_amount_on_a_pending_deal_via_polling_auto_voids_and_recreates()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Changed_amount_on_a_pending_deal_via_polling_auto_voids_and_recreates), tenantId);
        SeedConnection(h.Db, tenantId, HubSpotConnectionStatus.Connected);
        SeedPayee(h.Db, tenantId, "alice@example.com");
        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 5000m, new DateOnly(2026, 6, 1)) });
        await h.Job.SyncTenantAsync(tenantId, default);

        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 72409.39m, new DateOnly(2026, 6, 1)) });
        await h.Job.SyncTenantAsync(tenantId, default);

        (await h.Db.CompensationTransactions.CountAsync(t => t.Status == CompensationTransactionStatus.Cancelled))
            .Should().Be(1);
        var active = await h.Db.CompensationTransactions
            .SingleAsync(t => t.Status == CompensationTransactionStatus.Pending);
        active.Amount.Amount.Should().Be(72409.39m);
        (await h.Db.CrmDriftAlerts.AnyAsync()).Should().BeFalse();   // Pending fixed itself
    }

    [Fact]
    public async Task Changed_amount_on_a_paid_deal_via_polling_raises_an_alert_and_never_touches_it()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Changed_amount_on_a_paid_deal_via_polling_raises_an_alert_and_never_touches_it), tenantId);
        SeedConnection(h.Db, tenantId, HubSpotConnectionStatus.Connected);
        SeedPayee(h.Db, tenantId, "alice@example.com");
        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 5000m, new DateOnly(2026, 6, 1)) });
        await h.Job.SyncTenantAsync(tenantId, default);

        var tx = await h.Db.CompensationTransactions.SingleAsync();
        tx.MarkCalculated(1, Money.Of(250m, "USD"), "engine", new DateTimeOffset(Now), Guid.NewGuid());
        tx.MarkPaid("payrun", new DateTimeOffset(Now), Guid.NewGuid());
        await h.Db.SaveChangesAsync();

        SetupSource(h.DealSource, tenantId, new[] { Deal("101", 1m, new DateOnly(2026, 6, 1)) });
        await h.Job.SyncTenantAsync(tenantId, default);

        (await h.Db.CompensationTransactions.SingleAsync()).Status.Should().Be(CompensationTransactionStatus.Paid);
        (await h.Db.CompensationTransactions.SingleAsync()).Amount.Amount.Should().Be(5000m);
        (await h.Db.CrmDriftAlerts.CountAsync()).Should().Be(1);
    }

    // ── Resilience / skipping ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tenant_that_needs_reconnect_is_skipped_without_calling_hubspot()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Tenant_that_needs_reconnect_is_skipped_without_calling_hubspot), tenantId);
        SeedConnection(h.Db, tenantId, HubSpotConnectionStatus.NeedsReconnect);

        await h.Job.SyncTenantAsync(tenantId, default);   // must NOT throw

        await h.DealSource.DidNotReceive()
            .GetClosedWonDealsModifiedSinceAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        (await h.Db.CompensationTransactions.AnyAsync()).Should().BeFalse();
        (await h.Db.AuditLogs.AnyAsync(a => a.Action == "CRM_AUTO_SYNC_COMPLETED")).Should().BeFalse();
    }

    [Fact]
    public async Task Unusable_connection_mid_run_is_skipped_and_checkpoint_is_not_advanced()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Unusable_connection_mid_run_is_skipped_and_checkpoint_is_not_advanced), tenantId);
        SeedConnection(h.Db, tenantId, HubSpotConnectionStatus.Connected);
        h.DealSource.GetClosedWonDealsModifiedSinceAsync(tenantId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CrmDeal>>(_ => throw new CrmNotConnectedException(Source));

        await h.Job.SyncTenantAsync(tenantId, default);   // handled, no throw

        var conn = await h.Db.HubSpotConnections.IgnoreQueryFilters().SingleAsync();
        conn.LastSyncedAt.Should().BeNull();   // not advanced → next run re-tries the same window
    }

    [Fact]
    public async Task A_hard_failure_does_not_advance_the_checkpoint()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(A_hard_failure_does_not_advance_the_checkpoint), tenantId);
        SeedConnection(h.Db, tenantId, HubSpotConnectionStatus.Connected);
        h.DealSource.GetClosedWonDealsModifiedSinceAsync(tenantId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CrmDeal>>(_ => throw new InvalidOperationException("HubSpot 500"));

        var act = async () => await h.Job.SyncTenantAsync(tenantId, default);
        await act.Should().ThrowAsync<InvalidOperationException>();   // propagates → Hangfire retries this tenant only

        var conn = await h.Db.HubSpotConnections.IgnoreQueryFilters().SingleAsync();
        conn.LastSyncedAt.Should().BeNull();   // data not lost; next run re-processes from the old checkpoint
    }

    [Fact]
    public async Task Sync_is_tenant_isolated()
    {
        const string dbName = nameof(Sync_is_tenant_isolated);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var a = BuildHarness(dbName, tenantA);
        SeedConnection(a.Db, tenantA, HubSpotConnectionStatus.Connected);
        SeedPayee(a.Db, tenantA, "alice@example.com");
        SetupSource(a.DealSource, tenantA, new[] { Deal("777", 100m, new DateOnly(2026, 6, 1)) });

        var b = BuildHarness(dbName, tenantB);
        SeedConnection(b.Db, tenantB, HubSpotConnectionStatus.Connected);

        await a.Job.SyncTenantAsync(tenantA, default);

        var txA = await a.Db.CompensationTransactions.IgnoreQueryFilters().SingleAsync();
        txA.TenantId.Should().Be(tenantA);   // created under tenant A only, never B
    }
}
