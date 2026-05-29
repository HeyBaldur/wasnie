using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Entities;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.IntegrationTests.Infrastructure;

public sealed class TestDatabaseFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public TestWebApplicationFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Migrations must run before the factory starts.
        // Program.cs calls DbSeeder.SeedAsync at startup, which queries Identity tables —
        // those tables only exist after EF migrations have been applied.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options;

        await using (var db = new ApplicationDbContext(options, NoOpTenantContext.Instance, NoOpPublisher.Instance))
        {
            await db.Database.MigrateAsync();
            await SeedTestTenantsAsync(db);
        }

        // Factory startup (triggered by .Services) will call DbSeeder — tables exist now.
        Factory = new TestWebApplicationFactory(_container.GetConnectionString());
        _ = Factory.Services; // force startup
    }

    // Seed TenantA and TenantB as Enterprise tier so tier limits never block regular tests.
    private static async Task SeedTestTenantsAsync(ApplicationDbContext db)
    {
        var tenantIds = new[] { TestConstants.TenantA, TestConstants.TenantB };
        foreach (var id in tenantIds)
        {
            if (!await db.Tenants.AnyAsync(t => t.Id == id))
            {
                var tenant = Tenant.Create($"Test Tenant {id}", id.ToString("N")[..20], id, DateTimeOffset.UtcNow);
                tenant.SetTier(Tier.Enterprise);
                db.Tenants.Add(tenant);
            }
        }
        await db.SaveChangesAsync();
    }

    public async Task ResetAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM CompensationPlans");
    }

    public async Task ResetPayeesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Delete in dependency order
        await db.Database.ExecuteSqlRawAsync("DELETE FROM PlanAssignments");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Quotas");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Payees");
    }

    public async Task ResetRefreshTokensAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM RefreshTokens");
    }

    public async Task ResetCompensationDataAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM PlanAssignments");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Quotas");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Payees");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM CompensationPlans");
    }

    public async Task ResetTransactionsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM CompensationTransactions");
    }

    public async Task ResetImportsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ImportAudits");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM PlanAssignments");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Quotas");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Payees");
    }

    public async Task ResetTransactionImportDataAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // AuditLogs cannot be deleted (immutability trigger). Tests filter by TenantId + ResourceId instead.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ImportAudits");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM CompensationTransactions");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM BackgroundJobRecords");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM PlanAssignments");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Quotas");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Payees WHERE EmployeeCode LIKE 'TEST-%' OR EmployeeCode LIKE 'EMP%'");
    }

    public async Task DisposeAsync()
    {
        Factory.Dispose();
        await _container.DisposeAsync();
    }

    private sealed class NoOpTenantContext : ITenantContext
    {
        public static readonly NoOpTenantContext Instance = new();
        public Guid TenantId => Guid.Empty;
        public bool IsResolved => false;
    }

    private sealed class NoOpPublisher : IPublisher
    {
        public static readonly NoOpPublisher Instance = new();
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }
}
