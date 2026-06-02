using MediatR;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.IntegrationTests.Compensation;

public sealed class CreditAllocationServiceFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        var options = BuildOptions(Guid.Empty);
        await using var db = new ApplicationDbContext(options, new FixedTenantContext(Guid.Empty), NoOpPublisher.Instance);
        await db.Database.MigrateAsync();
    }

    public ApplicationDbContext CreateDbForTenant(Guid tenantId) =>
        new(BuildOptions(tenantId), new FixedTenantContext(tenantId), NoOpPublisher.Instance);

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private DbContextOptions<ApplicationDbContext> BuildOptions(Guid tenantId) =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

    internal sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool IsResolved => true;
    }

    private sealed class NoOpPublisher : IPublisher
    {
        public static readonly NoOpPublisher Instance = new();
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }
}
