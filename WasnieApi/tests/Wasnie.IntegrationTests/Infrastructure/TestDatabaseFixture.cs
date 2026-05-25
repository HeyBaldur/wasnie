using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Wasnie.Application.Common.Interfaces;
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
        }

        // Factory startup (triggered by .Services) will call DbSeeder — tables exist now.
        Factory = new TestWebApplicationFactory(_container.GetConnectionString());
        _ = Factory.Services; // force startup
    }

    public async Task ResetAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM CompensationPlans");
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
