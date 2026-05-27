using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.DTOs;
using Wasnie.Domain.Audit;
using Wasnie.Infrastructure.Persistence;
using Wasnie.Infrastructure.Services.Audit;
using Wasnie.IntegrationTests.Infrastructure;
using Wasnie.IntegrationTests.TestDoubles;

namespace Wasnie.IntegrationTests.Services.Audit;

public sealed class AuditServiceTests
{
    private static readonly Guid TenantA = TestConstants.TenantA;
    private static readonly Guid TenantB = TestConstants.TenantB;

    private static ApplicationDbContext CreateDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContext(tenantId), NoOpPublisher.Instance);
    }

    [Fact]
    public async Task LogAsync_WritesAuditLogRecord()
    {
        await using var db = CreateDb(TenantA);
        var dispatcher = new SyncAuditDispatcher(db, new FakeClock());
        var sut = new AuditService(dispatcher);

        await sut.LogAsync(new AuditEntry(
            TenantId: TenantA,
            Action: AuditActions.PayeeCreated,
            ResourceType: ResourceTypes.Payee,
            ResourceId: "payee-123",
            ActorUserId: "user-1",
            ActorEmail: "user@test.com",
            DisplayName: "Alice Smith"));

        var logs = await db.AuditLogs.IgnoreQueryFilters().ToListAsync();
        logs.Should().HaveCount(1);
    }

    [Fact]
    public async Task LogAsync_AuditRecord_HasCorrectFields()
    {
        await using var db = CreateDb(TenantA);
        var clock = new FakeClock();
        var dispatcher = new SyncAuditDispatcher(db, clock);
        var sut = new AuditService(dispatcher);

        await sut.LogAsync(new AuditEntry(
            TenantId: TenantA,
            Action: AuditActions.PlanActivated,
            ResourceType: ResourceTypes.Plan,
            ResourceId: "plan-456",
            ActorUserId: "actor-id",
            ActorEmail: "actor@tenant.com",
            DisplayName: "Q1 Sales Plan"));

        var log = (await db.AuditLogs.IgnoreQueryFilters().ToListAsync()).Single();
        log.TenantId.Should().Be(TenantA);
        log.Action.Should().Be(AuditActions.PlanActivated);
        log.ResourceType.Should().Be(ResourceTypes.Plan);
        log.ResourceId.Should().Be("plan-456");
        log.ActorUserId.Should().Be("actor-id");
        log.ActorEmail.Should().Be("actor@tenant.com");
        log.ResourceDisplayName.Should().Be("Q1 Sales Plan");
        log.TimestampUtc.Should().Be(clock.UtcNow);
    }

    [Fact]
    public async Task LogAsync_WithBeforeAndAfterJson_StoresStrings()
    {
        await using var db = CreateDb(TenantA);
        var dispatcher = new SyncAuditDispatcher(db, new FakeClock());
        var sut = new AuditService(dispatcher);

        await sut.LogAsync(new AuditEntry(
            TenantId: TenantA,
            Action: AuditActions.PayeeUpdated,
            ResourceType: ResourceTypes.Payee,
            ResourceId: "payee-789",
            ActorUserId: "u1",
            ActorEmail: "u1@test.com",
            Before: "{\"name\":\"Old\"}",
            After: "{\"name\":\"New\"}"));

        var log = (await db.AuditLogs.IgnoreQueryFilters().ToListAsync()).Single();
        log.BeforeJson.Should().Be("{\"name\":\"Old\"}");
        log.AfterJson.Should().Be("{\"name\":\"New\"}");
    }

    [Fact]
    public async Task LogAsync_TenantIsolation_TenantBCannotSeeTenantALogs()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbA = new ApplicationDbContext(options, new FixedTenantContext(TenantA), NoOpPublisher.Instance);
        var sutA = new AuditService(new SyncAuditDispatcher(dbA, new FakeClock()));

        await sutA.LogAsync(new AuditEntry(
            TenantId: TenantA, Action: AuditActions.LoginSuccess, ResourceType: ResourceTypes.Auth,
            ResourceId: "user-a", ActorUserId: "user-a", ActorEmail: "a@test.com"));

        await using var dbB = new ApplicationDbContext(options, new FixedTenantContext(TenantB), NoOpPublisher.Instance);
        var visibleToB = await dbB.AuditLogs.ToListAsync();
        visibleToB.Should().BeEmpty("tenant B must not see tenant A's audit logs");
    }
}
