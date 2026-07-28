using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used ONLY by the EF Core tooling (<c>dotnet ef migrations add</c> /
/// <c>database update</c>). It lets migrations be generated/applied by building just this Infrastructure
/// project, WITHOUT building the Wasnie.Api host — which matters because a running dev API locks its own
/// output DLLs. It reads the same connection string the API uses (env var override, else the Api's
/// appsettings). Never instantiated at runtime (DI supplies the real ApplicationDbContext).
/// </summary>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? ReadConnectionStringFromApiSettings()
            ?? throw new InvalidOperationException(
                "DefaultConnection not found for design-time. Set ConnectionStrings__DefaultConnection, or run " +
                "'dotnet ef' from the Wasnie.Infrastructure directory so the Api appsettings can be located.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString, b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .Options;

        return new ApplicationDbContext(options, DesignTimeTenantContext.Instance, DesignTimePublisher.Instance);
    }

    /// <summary>Reads ConnectionStrings:DefaultConnection from the Api's appsettings without pulling in the
    /// Microsoft.Extensions.Configuration packages (this project has none). Dev-only best effort.</summary>
    private static string? ReadConnectionStringFromApiSettings()
    {
        var apiDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "Wasnie.Api"));
        foreach (var file in new[] { "appsettings.Development.json", "appsettings.json" })
        {
            var path = Path.Combine(apiDir, file);
            if (!File.Exists(path)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("ConnectionStrings", out var cs)
                    && cs.TryGetProperty("DefaultConnection", out var dc)
                    && dc.ValueKind == JsonValueKind.String)
                {
                    var value = dc.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
            catch (JsonException) { /* try the next file */ }
        }
        return null;
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public static readonly DesignTimeTenantContext Instance = new();
        public Guid TenantId => Guid.Empty;
        public bool IsResolved => false;
    }

    private sealed class DesignTimePublisher : IPublisher
    {
        public static readonly DesignTimePublisher Instance = new();
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }
}
