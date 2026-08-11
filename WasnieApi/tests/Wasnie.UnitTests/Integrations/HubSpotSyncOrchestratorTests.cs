using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.Core;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Entities;
using Wasnie.Domain.Integrations.HubSpot;
using Wasnie.Infrastructure.BackgroundJobs;
using Wasnie.Infrastructure.Identity;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Integrations;

/// <summary>
/// Phase-3 orchestrator: lists Connected tenants and fans out STAGGERED per-tenant jobs (anti
/// thundering-herd). Skips non-Connected tenants; honours the Enabled switch.
/// </summary>
public sealed class HubSpotSyncOrchestratorTests
{
    private static readonly DateTimeOffset ConnectedAt = new(new DateTime(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc));

    private static ApplicationDbContext NewDb(string name)
    {
        var tenantCtx = new BackgroundJobTenantContext();
        tenantCtx.SetTenant(Guid.NewGuid()); // arbitrary; orchestrator reads with IgnoreQueryFilters anyway.
        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());
    }

    // The orchestrator now joins connections against the tenant's TIER, so a connection with no tenant
    // row behind it is (correctly) skipped. Every seeded tenant is paid unless a test says otherwise.
    private static void SeedTenant(ApplicationDbContext db, Guid tenantId, Tier tier = Tier.Growth)
    {
        var tenant = Tenant.Create($"T{tenantId:N}", $"t-{tenantId:N}", tenantId, ConnectedAt);
        tenant.SetTier(tier);
        db.Tenants.Add(tenant);
        db.SaveChanges();
    }

    private static void SeedConnection(
        ApplicationDbContext db, Guid tenantId, HubSpotConnectionStatus status, Tier tier = Tier.Growth)
    {
        SeedTenant(db, tenantId, tier);
        var c = HubSpotConnection.Create(
            Guid.NewGuid(), tenantId, 1, "a", "r", ConnectedAt.AddHours(1), "owner", ConnectedAt);
        if (status == HubSpotConnectionStatus.NeedsReconnect)
            c.MarkNeedsReconnect("x", ConnectedAt.AddDays(1));
        else if (status == HubSpotConnectionStatus.Disconnected)
            c.Disconnect("x", "owner", ConnectedAt.AddDays(1));
        db.HubSpotConnections.Add(c);
        db.SaveChanges();
    }

    private static HubSpotSyncOrchestrator NewOrchestrator(
        ApplicationDbContext db, IBackgroundJobClient client, HubSpotSyncOptions opts) =>
        new(db, client, Options.Create(opts), NullLogger<HubSpotSyncOrchestrator>.Instance);

    private static List<ICall> CreateCalls(IBackgroundJobClient client) =>
        client.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IBackgroundJobClient.Create)).ToList();

    [Fact]
    public async Task A_connected_tenant_on_Free_is_never_scheduled_and_keeps_its_connection()
    {
        // ★ The loop that spends money. A tenant that connected HubSpot while paying and then
        // downgraded must stop costing us outbound calls every hour — without anyone logging in, and
        // without their stored connection being destroyed (they get it back by upgrading).
        var db = NewDb(nameof(A_connected_tenant_on_Free_is_never_scheduled_and_keeps_its_connection));
        var paid = Guid.NewGuid();
        var downgraded = Guid.NewGuid();
        SeedConnection(db, paid, HubSpotConnectionStatus.Connected);
        SeedConnection(db, downgraded, HubSpotConnectionStatus.Connected, Tier.Free);

        var client = Substitute.For<IBackgroundJobClient>();
        await NewOrchestrator(db, client, new HubSpotSyncOptions { Enabled = true, TenantStaggerSeconds = 5 })
            .RunAsync(default);

        var scheduled = CreateCalls(client)
            .Select(c => (Guid)((Job)c.GetArguments()[0]!).Args[0]!)
            .ToList();

        scheduled.Should().ContainSingle().Which.Should().Be(paid);
        scheduled.Should().NotContain(downgraded, "Free tenants are dropped before a job is even created");

        db.HubSpotConnections.IgnoreQueryFilters()
            .Should().Contain(c => c.TenantId == downgraded && c.Status == HubSpotConnectionStatus.Connected,
                "frozen, not deleted — the row survives the downgrade");
    }

    [Fact]
    public async Task Schedules_one_staggered_job_per_connected_tenant_and_skips_the_rest()
    {
        var db = NewDb(nameof(Schedules_one_staggered_job_per_connected_tenant_and_skips_the_rest));
        var connected = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var t in connected) SeedConnection(db, t, HubSpotConnectionStatus.Connected);
        SeedConnection(db, Guid.NewGuid(), HubSpotConnectionStatus.NeedsReconnect);
        SeedConnection(db, Guid.NewGuid(), HubSpotConnectionStatus.Disconnected);

        var client = Substitute.For<IBackgroundJobClient>();
        await NewOrchestrator(db, client, new HubSpotSyncOptions { Enabled = true, TenantStaggerSeconds = 5 })
            .RunAsync(default);

        var calls = CreateCalls(client);
        calls.Should().HaveCount(3); // only the 3 Connected tenants

        // Each call schedules a SyncTenantAsync for a connected tenant.
        var scheduledTenantIds = calls
            .Select(c => (Guid)((Job)c.GetArguments()[0]!).Args[0]!)
            .ToList();
        scheduledTenantIds.Should().BeEquivalentTo(connected);

        // Staggered: the three scheduled-enqueue times are all distinct and strictly increasing.
        var enqueueAts = calls.Select(c => ((ScheduledState)c.GetArguments()[1]!).EnqueueAt).ToList();
        enqueueAts.Should().OnlyHaveUniqueItems();
        enqueueAts.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Does_nothing_when_disabled()
    {
        var db = NewDb(nameof(Does_nothing_when_disabled));
        SeedConnection(db, Guid.NewGuid(), HubSpotConnectionStatus.Connected);

        var client = Substitute.For<IBackgroundJobClient>();
        await NewOrchestrator(db, client, new HubSpotSyncOptions { Enabled = false }).RunAsync(default);

        CreateCalls(client).Should().BeEmpty();
    }

    [Fact]
    public async Task Schedules_nothing_when_no_tenant_is_connected()
    {
        var db = NewDb(nameof(Schedules_nothing_when_no_tenant_is_connected));
        SeedConnection(db, Guid.NewGuid(), HubSpotConnectionStatus.Disconnected);

        var client = Substitute.For<IBackgroundJobClient>();
        await NewOrchestrator(db, client, new HubSpotSyncOptions { Enabled = true }).RunAsync(default);

        CreateCalls(client).Should().BeEmpty();
    }
}
