using MediatR;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Audit;

/// <summary>
/// Self-contained test fixture for money-critical audit transaction tests.
/// Uses its own MsSql container (isolated from the shared integration test fixture).
/// </summary>
public sealed class MoneyAuditTestFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        var options = BuildOptions();
        await using var db = new ApplicationDbContext(options, NoOpTenantContext.Instance, NoOpPublisher.Instance);
        await db.Database.MigrateAsync();
    }

    public ApplicationDbContext CreateDb(Guid tenantId) =>
        new(BuildOptions(), new FixedTenantContext(tenantId), NoOpPublisher.Instance);

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private DbContextOptions<ApplicationDbContext> BuildOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

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
