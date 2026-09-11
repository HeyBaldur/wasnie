using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Sandbox;

/// <summary>
/// Lleva a producción la configuración que el usuario encontró probando: el plan y sus reglas, creados
/// de verdad con los comandos reales.
/// </summary>
/// <remarks>
/// ★★ ES EL ÚNICO SITIO DONDE LOS DOS ESQUEMAS SE TOCAN, Y POR ESO TIENE NOMBRE PROPIO. En todo el
/// resto del producto la separación es total; aquí hay una lectura de práctica y una escritura real
/// en el mismo método, a propósito y a la vista. Escondido dentro de un handler compartido, con una
/// bandera decidiendo el lado, sería justo el diseño que este ticket existe para no tener.
///
/// ★★ COPIA, NO MUEVE. El experimento sigue en el sandbox después de promover: el usuario puede
/// seguir jugando con él, resetearlo o promoverlo otra vez. Nada de lo que cruza se borra del origen.
///
/// ★★ NACE EN BORRADOR, Y NO PORQUE AQUÍ SE ELIJA. Lo despacha <see cref="CreatePlanCommand"/>, y
/// <c>Plan.Create</c> pone <c>Draft</c>. Un plan promovido no paga a nadie hasta que una persona lo
/// active desde la pantalla real de planes — la promoción trae la configuración, no la decisión de
/// empezar a pagar con ella.
/// </remarks>
public sealed record PromoteSandboxPlanCommand : IRequest<Result<PromotedPlanDto>>;

/// <param name="Name">El nombre con el que quedó, que puede no ser el del sandbox: ver el sufijo.</param>
/// <param name="OriginalName">Con el que estaba en el recorrido, para poder decir que se renombró.</param>
public sealed record PromotedPlanDto(
    Guid PlanId,
    string Name,
    string OriginalName,
    int RuleCount);

public sealed class PromoteSandboxPlanHandler(
    ISandboxPlanSource sandboxPlans,
    ISandboxScope sandboxScope,
    IApplicationDbContext db,
    IMediator mediator,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IAuditService auditService)
    : IRequestHandler<PromoteSandboxPlanCommand, Result<PromotedPlanDto>>
{
    public async Task<Result<PromotedPlanDto>> Handle(
        PromoteSandboxPlanCommand request, CancellationToken cancellationToken)
    {
        // ★★ LA GUARDA NO ES PARANOIA, ES LO QUE PASA SI ALGUIEN MUEVE ESTE ENDPOINT. El contexto de
        // datos que reciben los comandos reales se elige por el ámbito de la petición; si esta acción
        // acabara algún día colgando de `api/sandbox`, el filtro entraría al sandbox y la «promoción»
        // crearía el plan otra vez en el esquema de práctica. No se rompería nada, y ese es el
        // problema: el usuario vería «promovido» y no habría plan real por ninguna parte. Aquí revienta.
        if (sandboxScope.IsSandbox)
        {
            throw new InvalidOperationException(
                "Promotion writes to the real schema and must never run inside the sandbox scope. " +
                "This endpoint must not sit behind EnterSandboxFilter.");
        }

        var config = await sandboxPlans.GetCurrentPlanAsync(cancellationToken);

        if (config is null)
        {
            return Result<PromotedPlanDto>.Failure("There is no sandbox plan to promote yet.");
        }

        if (config.Rules.Count == 0)
        {
            return Result<PromotedPlanDto>.Failure(
                "The sandbox plan has no active rules. A plan with no rules pays nothing.");
        }

        // ★★ LAS TABLAS SE VALIDAN ANTES DE ESCRIBIR NADA. Cada regla vuelve al dominio por su fábrica,
        // que es donde viven las invariantes de la escalera. Hacerlo aquí, y no regla a regla después
        // de crear el plan, evita el único desenlace feo de esta operación: un plan real a medio poblar
        // porque la cuarta regla no pasaba. Si algo no cruza, todavía no se ha creado nada.
        var ruleRequests = new List<(SandboxRuleConfiguration Rule, RateTableRequest Table)>();

        foreach (var rule in config.Rules)
        {
            var table = ToRequest(rule.RateTable);

            try
            {
                _ = table.ToDomain();
            }
            catch (DomainCodedException)
            {
                // El código y sus parámetros llegan al navegador tal cual, traducibles. Nada escrito.
                throw;
            }
            catch (DomainException ex)
            {
                return Result<PromotedPlanDto>.Failure($"Rule '{rule.Name}' cannot be promoted: {ex.Message}");
            }

            ruleRequests.Add((rule, table));
        }

        var name = await AvailableNameAsync(config.Name, cancellationToken);

        // Desde aquí, los comandos REALES. Cada uno con su permiso, su cupo del plan contratado y su
        // auditoría: promover es crear un plan, con exactamente las mismas consecuencias.
        var created = await mediator.Send(
            new CreatePlanCommand(name, config.Description, config.EffectiveStart, config.EffectiveEnd, config.Currency),
            cancellationToken);

        if (!created.IsSuccess || created.Value is null)
        {
            return Result<PromotedPlanDto>.Failure(created.Error ?? "The plan could not be created.");
        }

        var planId = created.Value.Id;

        foreach (var (rule, table) in ruleRequests)
        {
            var added = await mediator.Send(
                new AddRuleToPlanCommand(
                    planId, rule.Name, rule.SortOrder, rule.Measurement, table,
                    rule.Trigger, rule.Modifier, rule.Cap, rule.Floor),
                cancellationToken);

            if (!added.IsSuccess)
            {
                // ★ SE DICE LO QUE QUEDÓ, NO SE FINGE QUE NO PASÓ. Con las tablas ya validadas arriba
                // esto es casi imposible, pero si ocurre hay un plan real en borrador con menos reglas
                // de las que el usuario probó. Callarlo lo dejaría creyendo que promovió su
                // configuración entera; el plan queda en Draft y no paga, y el mensaje dice dónde mirar.
                return Result<PromotedPlanDto>.Failure(
                    $"Plan '{name}' was created as a draft, but rule '{rule.Name}' could not be added: {added.Error}");
            }
        }

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantContext.TenantId,
                Action: AuditActions.PlanPromotedFromSandbox,
                ResourceType: ResourceTypes.Plan,
                ResourceId: planId.ToString(),
                ActorUserId: currentUser.UserId ?? "system",
                ActorEmail: currentUser.Email ?? string.Empty,
                DisplayName: name), cancellationToken);
        }
        catch { /* audit failures must not block the user operation */ }

        return Result<PromotedPlanDto>.Success(
            new PromotedPlanDto(planId, name, config.Name, ruleRequests.Count));
    }

    /// <summary>
    /// El nombre libre más cercano al del sandbox: el mismo si nadie lo usa, y con sufijo si sí.
    /// </summary>
    /// <remarks>
    /// ★ EL SUFIJO SE DEVUELVE, NO SÓLO SE APLICA. Renombrar en silencio dejaría al usuario buscando
    /// «Plan de ventas» en una lista donde lo que hay es «Plan de ventas (2)». La pantalla dice con qué
    /// nombre quedó, y por eso el DTO lleva también el original.
    /// </remarks>
    private async Task<string> AvailableNameAsync(string desired, CancellationToken cancellationToken)
    {
        var taken = await db.CompensationPlans
            .AsNoTracking()
            .Where(p => p.Name == desired || p.Name.StartsWith(desired + " ("))
            .Select(p => p.Name)
            .ToListAsync(cancellationToken);

        if (!taken.Contains(desired))
        {
            return desired;
        }

        for (var n = 2; n < 1000; n++)
        {
            var candidate = $"{desired} ({n})";

            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        // Mil planes con el mismo nombre no es un caso a resolver; lo único que importa aquí es no
        // devolver un nombre que ya está puesto.
        return $"{desired} ({Guid.NewGuid():N})";
    }

    /// <summary>
    /// La tabla guardada, de vuelta a la forma de entrada.
    /// </summary>
    /// <remarks>
    /// ★★ EL RODEO ES DELIBERADO: LA TABLA VUELVE A ENTRAR POR LA PUERTA. Pasar el objeto de dominio
    /// tal cual al comando saltaría las fábricas —que es exactamente el defecto que
    /// <see cref="RateTableRequest"/> existe para haber arreglado— y una escalera rota del sandbox
    /// aterrizaría en un plan real sin que nada la mirara. Reconstruir el request obliga a que
    /// <c>ToDomain</c> corra otra vez, ya en el camino de escritura real.
    /// </remarks>
    private static RateTableRequest ToRequest(RateTable table) => new(
        table.Type,
        table.FlatRate,
        table.Tiers?.Select(t => new RateTierRequest(t.From, t.To, t.Rate)).ToList(),
        table.AttainmentTiers?
            .Select(t => new AttainmentTierRequest(t.AttainmentFrom, t.AttainmentTo, t.Rate)).ToList(),
        table.SplitAtQuota);
}
