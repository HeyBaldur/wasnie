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

    private static void SeedConnection(ApplicationDbContext db, Guid tenantId, HubSpotConnectionStatus status)
    {
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
