using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Integrations.Crm;
using Wasnie.Application.Integrations.HubSpot;
using Wasnie.Domain.Integrations.HubSpot;
using Wasnie.Infrastructure.Persistence;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.UnitTests.Integrations;

/// <summary>Phase-3 "Sync now": only enqueues when the tenant has a usable (Connected) HubSpot connection.</summary>
public sealed class TriggerHubSpotSyncHandlerTests
{
    private static readonly DateTimeOffset Now = new(new DateTime(2026, 6, 23, 10, 0, 0, DateTimeKind.Utc));

    private static (ApplicationDbContext db, TriggerHubSpotSyncHandler handler, ICrmSyncScheduler scheduler) Build(
        string dbName, Guid tenantId)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());
        var scheduler = Substitute.For<ICrmSyncScheduler>();
        var handler = new TriggerHubSpotSyncHandler(db, tenantCtx, Substitute.For<IAuthorizationService>(), scheduler);
        return (db, handler, scheduler);
    }

    private static void SeedConnection(ApplicationDbContext db, Guid tenantId, HubSpotConnectionStatus status)
    {
        var c = HubSpotConnection.Create(Guid.NewGuid(), tenantId, 1, "a", "r", Now.AddHours(1), "owner", Now);
        if (status == HubSpotConnectionStatus.NeedsReconnect) c.MarkNeedsReconnect("x", Now);
        db.HubSpotConnections.Add(c);
        db.SaveChanges();
    }

    [Fact]
    public async Task Connected_tenant_enqueues_an_immediate_sync()
    {
        var tenantId = Guid.NewGuid();
        var (db, handler, scheduler) = Build(nameof(Connected_tenant_enqueues_an_immediate_sync), tenantId);
        SeedConnection(db, tenantId, HubSpotConnectionStatus.Connected);

        var result = await handler.Handle(new TriggerHubSpotSyncCommand(), default);

        result.IsSuccess.Should().BeTrue();
        scheduler.Received(1).EnqueueTenantSyncNow(tenantId);
    }

    [Fact]
    public async Task Without_a_usable_connection_it_fails_and_enqueues_nothing()
    {
        var tenantId = Guid.NewGuid();
        var (db, handler, scheduler) = Build(nameof(Without_a_usable_connection_it_fails_and_enqueues_nothing), tenantId);
        SeedConnection(db, tenantId, HubSpotConnectionStatus.NeedsReconnect);

        var result = await handler.Handle(new TriggerHubSpotSyncCommand(), default);

        result.IsSuccess.Should().BeFalse();
        scheduler.DidNotReceive().EnqueueTenantSyncNow(Arg.Any<Guid>());
    }
}
