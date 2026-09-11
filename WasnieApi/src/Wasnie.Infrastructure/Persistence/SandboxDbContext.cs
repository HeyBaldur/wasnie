using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Infrastructure.Persistence;

/// <summary>
/// El mismo modelo que <see cref="ApplicationDbContext"/>, con las tablas del ciclo de compensación
/// en el esquema <c>Sandbox</c>.
///
/// ★★ NO REIMPLEMENTA NADA, Y AHÍ ESTÁ TODO EL VALOR. El onboarding guiado (KAN-68) tiene que enseñar
/// el ciclo real: crear un plan, una regla, un payee, una venta, calcular, correr un pay run, aprobar,
/// pagar. Si tuviera su propia lógica, el día que cambie el motor el onboarding seguiría enseñando el
/// motor viejo — es decir, mintiendo. Lo que cambia aquí no es la lógica: es el DESTINO de los datos.
/// Los handlers no se enteran de nada porque piden <see cref="IApplicationDbContext"/> y esa interfaz
/// se resuelve a uno u otro contexto según el momento.
///
/// ★★ LA SEPARACIÓN ES ESTRUCTURAL, NO UNA REGLA QUE HAYA QUE RECORDAR. La alternativa —una bandera
/// `isSandbox` sobre las tablas reales— depende de que CADA consulta del sistema se acuerde de
/// filtrarla; una sola olvidada mezcla dinero de mentira con dinero de verdad. Con esquemas separados
/// el pay run real consulta `Credits` y no existe forma de que alcance `Sandbox.Credits`.
///
/// ★ COMPARTE LO QUE NO ES DINERO. Tenant, usuarios, suscripción y auditoría siguen apuntando al
/// esquema real: el sandbox necesita saber de qué empresa y de qué persona es lo que crea. La lista de
/// lo que sí se duplica vive en <see cref="ApplicationDbContext"/> y es la frontera de seguridad.
/// </summary>
public sealed class SandboxDbContext(
    DbContextOptions<SandboxDbContext> options,
    ITenantContext tenantContext,
    IPublisher publisher)
    : ApplicationDbContext(options, tenantContext, publisher)
{
    /// <summary>El esquema del sandbox. Las tablas conservan su nombre: <c>Sandbox.Credits</c>, etc.</summary>
    public const string SchemaName = "Sandbox";

    /// <summary>La tabla de historial de migraciones propia — ver la nota del registro en DependencyInjection.</summary>
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    protected override string? CompensationSchema => SchemaName;

    /// <summary>El historial de experimentos. Sólo existe aquí — ver la nota del modelo.</summary>
    public DbSet<Wasnie.Domain.Sandbox.SandboxExperiment> Experiments => Set<Wasnie.Domain.Sandbox.SandboxExperiment>();
}
