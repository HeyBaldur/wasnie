using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Quotas;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Entities;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Infrastructure;

/// <summary>
/// La frontera entre el dinero real y el de práctica (KAN-68).
///
/// ★★ ESTO PRUEBA LA GARANTÍA, NO EL CÓDIGO QUE DEBERÍA DARLA. Todo el diseño del onboarding se apoya
/// en una afirmación: un pay run real consulta `Credits` y NO PUEDE alcanzar los créditos de práctica.
/// Esa afirmación vive en el modelo de EF, así que se comprueba en el modelo — que es lo que la base
/// de datos va a obedecer — y no en una consulta de ejemplo que podría estar tomando otro camino.
///
/// ★ SIN BASE DE DATOS. Se construye el modelo y se lee su metadato: es una prueba de unidad de
/// verdad, rápida y sin contenedores.
/// </summary>
public sealed class SandboxSchemaSeparationTests
{
    /// <remarks>
    /// Cada contexto recibe SUS opciones tipadas, igual que en producción: con el tipo no genérico, EF
    /// se niega a construirlos cuando hay más de un contexto — y eso sólo se ve al arrancar.
    /// </remarks>
    private static DbContextOptions<T> OptionsFor<T>() where T : DbContext =>
        new DbContextOptionsBuilder<T>()
            .UseSqlServer("Server=(unused);Database=(unused);Trusted_Connection=True;")
            .Options;

    private static IModel RealModel()
    {
        using var context = new ApplicationDbContext(
            OptionsFor<ApplicationDbContext>(), StubTenant.Instance, StubPublisher.Instance);
        return context.Model;
    }

    private static IModel SandboxModel()
    {
        using var context = new SandboxDbContext(
            OptionsFor<SandboxDbContext>(), StubTenant.Instance, StubPublisher.Instance);
        return context.Model;
    }

    public static TheoryData<Type> MoneyEntities => new()
    {
        typeof(Credit), typeof(CompensationPayout), typeof(PayRun), typeof(Wasnie.Domain.Compensation.Plans.Plan),
        typeof(CompensationTransaction), typeof(Payee), typeof(Quota), typeof(PlanAssignment),
    };

    [Theory]
    [MemberData(nameof(MoneyEntities))]
    public void Money_tables_live_in_the_sandbox_schema_for_the_sandbox_context(Type entity)
    {
        SandboxModel().FindEntityType(entity)!.GetSchema()
            .Should().Be(SandboxDbContext.SchemaName, "el ciclo de compensación es lo único que el sandbox duplica");
    }

    [Theory]
    [MemberData(nameof(MoneyEntities))]
    public void The_real_context_keeps_the_money_tables_where_they_were(Type entity)
    {
        RealModel().FindEntityType(entity)!.GetSchema()
            .Should().BeNull("el producto real no se entera de que existe un sandbox");
    }

    /// <summary>
    /// ★★ LA MITAD QUE SE OLVIDA: lo que NO se duplica. El sandbox tiene que leer la empresa y la
    /// persona de verdad — si el tenant se duplicara, el usuario practicaría dentro de una empresa
    /// fantasma y nada de lo que viera se parecería a la suya.
    /// </summary>
    [Theory]
    [InlineData(typeof(Tenant))]
    [InlineData(typeof(Wasnie.Domain.Audit.AuditLog))]
    [InlineData(typeof(Wasnie.Domain.Subscription.UserSubscription))]
    public void Shared_tables_are_not_duplicated_into_the_sandbox(Type entity)
    {
        SandboxModel().FindEntityType(entity)!.GetSchema()
            .Should().BeNull("el tenant, la auditoría y la suscripción son los de verdad");
    }

    /// <summary>
    /// Las migraciones del sandbox sólo pueden crear las tablas del sandbox: si arrastraran las demás,
    /// la primera intentaría crear otra vez tablas que ya existen y no se aplicaría ninguna.
    /// </summary>
    /// <remarks>
    /// ★ SE PREGUNTA AL MODELO DE TIEMPO DE DISEÑO, que es el que leen las herramientas de migración. El
    /// modelo de ejecución está recortado para ir rápido y no guarda este dato — preguntárselo a él no da
    /// una respuesta equivocada, da una excepción, que al menos es honesta.
    /// </remarks>
    [Fact]
    public void Sandbox_migrations_exclude_every_shared_table()
    {
        using var context = new SandboxDbContext(
            OptionsFor<SandboxDbContext>(), StubTenant.Instance, StubPublisher.Instance);
        var model = context.GetService<IDesignTimeModel>().Model;

        model.FindEntityType(typeof(Tenant))!.IsTableExcludedFromMigrations().Should().BeTrue();
        model.FindEntityType(typeof(Credit))!.IsTableExcludedFromMigrations().Should().BeFalse();
    }

    private sealed class StubTenant : ITenantContext
    {
        public static readonly StubTenant Instance = new();
        public Guid TenantId => Guid.Empty;
        public bool IsResolved => false;
    }

    private sealed class StubPublisher : IPublisher
    {
        public static readonly StubPublisher Instance = new();
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }
}
