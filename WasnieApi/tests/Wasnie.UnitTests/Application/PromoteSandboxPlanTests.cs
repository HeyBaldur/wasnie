using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Sandbox;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// La promoción de un experimento a plan REAL (KAN-68, tanda 5).
///
/// ★★ ES LA ÚNICA OPERACIÓN DEL RECORRIDO QUE ESCRIBE EN EL LADO DEL DINERO, y por eso lo que se
/// prueba aquí no es que «funcione»: es que no escriba cuando no debe. Que la guarda del ámbito
/// reviente, que una escalera rota se rechace ANTES de crear nada, y que la configuración que cruza
/// sea la que el usuario probó y no otra.
/// </summary>
public sealed class PromoteSandboxPlanTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private sealed class StubScope(bool isSandbox) : ISandboxScope
    {
        public bool IsSandbox { get; private set; } = isSandbox;

        public void Enter() => IsSandbox = true;
    }

    private sealed class StubSource(SandboxPlanConfiguration? config) : ISandboxPlanSource
    {
        public Task<SandboxPlanConfiguration?> GetCurrentPlanAsync(CancellationToken cancellationToken) =>
            Task.FromResult(config);
    }

    private static SandboxRuleConfiguration Rule(string name, RateTable table) =>
        new(name, 1, new Measurement { Type = MeasurementType.Revenue, SourceField = "amount" },
            table, null, null, null, null);

    private static SandboxPlanConfiguration Config(string name, params SandboxRuleConfiguration[] rules) =>
        new(Guid.NewGuid(), name, "probado en el sandbox",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "EUR", rules);

    private static ApplicationDbContext RealDb(string dbName)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);

        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"{nameof(PromoteSandboxPlanTests)}.{dbName}")
                .Options,
            tenantCtx, Substitute.For<IPublisher>());
    }

    /// <summary>El mediador de verdad no se usa: lo que importa es QUÉ comandos se despachan y en qué orden.</summary>
    private sealed class RecordingMediator : IMediator
    {
        public List<object> Sent { get; } = [];

        /// <summary>Con qué falla el enésimo `AddRuleToPlanCommand`, para el caso feo.</summary>
        public string? RuleFailure { get; set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);

            object response = request switch
            {
                CreatePlanCommand c => Result<PlanDto>.Success(new PlanDto(
                    Guid.NewGuid(), TenantId, c.Name, c.Description, 1, "Draft",
                    c.EffectiveStart, c.EffectiveEnd, c.Currency, Now, "test", [], 0, null, null)),

                AddRuleToPlanCommand r when RuleFailure is not null =>
                    Result<RuleDto>.Failure(RuleFailure),

                AddRuleToPlanCommand r => Result<RuleDto>.Success(new RuleDto(
                    Guid.NewGuid(), r.Name, r.SortOrder, true,
                    r.Trigger ?? new object(), r.Measurement, r.RateTable, null, null, null)),

                _ => throw new NotSupportedException(request.GetType().Name),
            };

            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static PromoteSandboxPlanHandler Build(
        SandboxPlanConfiguration? config,
        ApplicationDbContext db,
        RecordingMediator mediator,
        bool inSandboxScope = false)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("U-1");
        currentUser.Email.Returns("u@example.com");

        return new PromoteSandboxPlanHandler(
            new StubSource(config),
            new StubScope(inSandboxScope),
            db,
            mediator,
            tenantCtx,
            currentUser,
            Substitute.For<IAuditService>());
    }

    /// <remarks>
    /// ★★ EL FALLO QUE ESTA PRUEBA EVITA NO SE VE EN PANTALLA. Si esta acción acabara corriendo dentro
    /// del ámbito de práctica, el «plan real» se crearía otra vez en el esquema del sandbox: la
    /// pantalla diría «promovido», no fallaría nada, y no habría plan real por ninguna parte.
    /// </remarks>
    [Fact]
    public async Task Refuses_to_run_inside_the_sandbox_scope()
    {
        var mediator = new RecordingMediator();
        using var db = RealDb(nameof(Refuses_to_run_inside_the_sandbox_scope));
        var handler = Build(Config("Plan", Rule("Flat", RateTable.Flat(0.05m))), db, mediator, inSandboxScope: true);

        var act = () => handler.Handle(new PromoteSandboxPlanCommand(), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        mediator.Sent.Should().BeEmpty("nada puede escribirse desde el lado equivocado");
    }

    [Fact]
    public async Task Refuses_when_there_is_nothing_to_promote()
    {
        var mediator = new RecordingMediator();
        using var db = RealDb(nameof(Refuses_when_there_is_nothing_to_promote));
        var handler = Build(null, db, mediator);

        var result = await handler.Handle(new PromoteSandboxPlanCommand(), default);

        result.IsSuccess.Should().BeFalse();
        mediator.Sent.Should().BeEmpty();
    }

    /// <remarks>Un plan real sin reglas no paga nada: crearlo sería dejarle al usuario un plan hueco.</remarks>
    [Fact]
    public async Task Refuses_a_plan_with_no_active_rules()
    {
        var mediator = new RecordingMediator();
        using var db = RealDb(nameof(Refuses_a_plan_with_no_active_rules));
        var handler = Build(Config("Plan sin reglas"), db, mediator);

        var result = await handler.Handle(new PromoteSandboxPlanCommand(), default);

        result.IsSuccess.Should().BeFalse();
        mediator.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Promotes_the_plan_and_every_rule()
    {
        var mediator = new RecordingMediator();
        using var db = RealDb(nameof(Promotes_the_plan_and_every_rule));
        var config = Config(
            "Plan de ventas",
            Rule("Plana", RateTable.Flat(0.05m)),
            Rule("Escalera", RateTable.Tiered([
                new RateTier { From = 0, To = 50000, Rate = 0.03m },
                new RateTier { From = 50000, To = null, Rate = 0.07m },
            ])));

        var result = await handler(config, db, mediator);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Name.Should().Be("Plan de ventas");
        result.Value.OriginalName.Should().Be("Plan de ventas");
        result.Value.RuleCount.Should().Be(2);

        mediator.Sent.OfType<CreatePlanCommand>().Should().ContainSingle()
            .Which.Currency.Should().Be("EUR");

        var rules = mediator.Sent.OfType<AddRuleToPlanCommand>().ToList();
        rules.Should().HaveCount(2);
        rules[0].RateTable.Type.Should().Be(RateTableType.Flat);
        rules[0].RateTable.FlatRate.Should().Be(0.05m);
        rules[1].RateTable.Tiers.Should().HaveCount(2);
    }

    /// <remarks>
    /// ★★ ESTA ES LA PRUEBA DE LA DECISIÓN DE ORDEN. Una escalera con un hueco no llega a producción, y
    /// —lo que de verdad importa— tampoco deja tras de sí un plan real a medio poblar: el rechazo
    /// ocurre antes de que se despache un solo comando de escritura.
    /// </remarks>
    [Fact]
    public async Task A_broken_ladder_is_refused_before_anything_is_written()
    {
        var mediator = new RecordingMediator();
        using var db = RealDb(nameof(A_broken_ladder_is_refused_before_anything_is_written));

        // Se construye por propiedades, como hace el deserializador: la fábrica rechazaría el hueco, y
        // el punto de la prueba es que una tabla ASÍ —guardada, ilegal— no cruce.
        var holed = new RateTable
        {
            Type = RateTableType.Tiered,
            Tiers =
            [
                new RateTier { From = 0, To = 100, Rate = 0.03m },
                new RateTier { From = 500, To = null, Rate = 0.07m },
            ],
        };

        // ★ SALE COMO EXCEPCIÓN CODIFICADA, NO COMO `Result.Failure`, Y ASÍ TIENE QUE SER: el código y
        // sus parámetros llegan al navegador en un 422 y se traducen al idioma del lector. Convertirlo
        // aquí en un texto perdería el código y el usuario leería una frase en inglés.
        var act = () => handler(Config("Plan roto", Rule("Con hueco", holed)), db, mediator);

        (await act.Should().ThrowAsync<DomainCodedException>()).Which.Code.Should().Be("RateTableTiersLeaveGap");
        mediator.Sent.Should().BeEmpty("un plan real a medio poblar es peor que no promover");
    }

    /// <remarks>El nombre elegido se devuelve: renombrar en silencio deja al usuario buscando un plan que no está.</remarks>
    [Fact]
    public async Task Suffixes_the_name_when_a_real_plan_already_has_it()
    {
        var mediator = new RecordingMediator();
        using var db = RealDb(nameof(Suffixes_the_name_when_a_real_plan_already_has_it));

        db.CompensationPlans.Add(Plan.Create(
            TenantId, "Plan de ventas", "el que ya existía",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "EUR", "seed", Guid.NewGuid(), Now, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var result = await handler(
            Config("Plan de ventas", Rule("Plana", RateTable.Flat(0.05m))), db, mediator);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Name.Should().Be("Plan de ventas (2)");
        result.Value.OriginalName.Should().Be("Plan de ventas");
        mediator.Sent.OfType<CreatePlanCommand>().Single().Name.Should().Be("Plan de ventas (2)");
    }

    /// <remarks>
    /// Si una regla no entra después de crear el plan, se dice — con el nombre del plan y el de la
    /// regla. Callarlo dejaría al usuario creyendo que promovió su configuración entera.
    /// </remarks>
    [Fact]
    public async Task Says_which_rule_failed_instead_of_reporting_success()
    {
        var mediator = new RecordingMediator { RuleFailure = "Only Per Transaction cap scope is currently supported." };
        using var db = RealDb(nameof(Says_which_rule_failed_instead_of_reporting_success));

        var result = await handler(Config("Plan", Rule("Plana", RateTable.Flat(0.05m))), db, mediator);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Plana");
    }

    private static Task<Result<PromotedPlanDto>> handler(
        SandboxPlanConfiguration config, ApplicationDbContext db, RecordingMediator mediator) =>
        Build(config, db, mediator).Handle(new PromoteSandboxPlanCommand(), default);
}
