using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Wasnie.Infrastructure.Persistence;

/// <summary>
/// La fábrica de tiempo de diseño del contexto del sandbox. Existe por el mismo motivo que la del
/// contexto real: permite generar y aplicar migraciones compilando SÓLO este proyecto, sin construir
/// el host de la API — que es lo que hay que hacer cuando la API de desarrollo está levantada y tiene
/// bloqueados sus propios DLL.
///
/// ★ MISMA BASE DE DATOS, DISTINTO ESQUEMA E HISTORIAL. La cadena de conexión se lee igual; lo que
/// cambia es que estas migraciones escriben en `Sandbox` y llevan su propia tabla de historial, para
/// que los dos contextos no se pisen creyendo cada uno que las migraciones del otro son suyas.
/// </summary>
public sealed class SandboxDbContextFactory : IDesignTimeDbContextFactory<SandboxDbContext>
{
    public SandboxDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? ApplicationDbContextFactory.ReadConnectionStringFromApiSettings()
            ?? throw new InvalidOperationException(
                "DefaultConnection not found for design-time. Set ConnectionStrings__DefaultConnection, or run " +
                "'dotnet ef' from the Wasnie.Infrastructure directory so the Api appsettings can be located.");

        var options = new DbContextOptionsBuilder<SandboxDbContext>()
            .UseSqlServer(connectionString, b => b
                .MigrationsAssembly(typeof(SandboxDbContext).Assembly.FullName)
                .MigrationsHistoryTable(SandboxDbContext.MigrationsHistoryTable, SandboxDbContext.SchemaName))
            .Options;

        return new SandboxDbContext(
            options,
            ApplicationDbContextFactory.DesignTimeTenantContext.Instance,
            ApplicationDbContextFactory.DesignTimePublisher.Instance);
    }
}
