using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Compensation.Commands.Assignments;
using Wasnie.Application.Compensation.Commands.PayRuns;
using Wasnie.Application.Compensation.Commands.Payees;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Compensation.Commands.Quotas;
using Wasnie.Application.Compensation.Commands.Transactions;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Application.Sandbox;

namespace Wasnie.Api.Controllers;

/// <summary>
/// El onboarding guiado: los mismos comandos del producto, escritos en el esquema <c>Sandbox</c>.
///
/// ★★ NO HAY UN SOLO COMANDO PROPIO AQUÍ, Y ES EL REQUISITO CENTRAL DEL TICKET. Cada acción despacha
/// exactamente el mismo comando que la pantalla real: crear un plan es `CreatePlanCommand`, correr un
/// pay run es `CalculatePayRunCommand`. Si el motor cambia mañana, el onboarding enseña lo nuevo el
/// mismo día — sin que nadie se acuerde de actualizarlo. Un onboarding con lógica propia enseña el
/// producto de hace seis meses, que es una forma cara de mentir.
///
/// ★★ LO ÚNICO QUE ESTE CONTROLADOR APORTA ES EL DESTINO. El filtro de abajo marca la petición como de
/// práctica ANTES de que la acción corra, y a partir de ahí la interfaz de datos que reciben los
/// handlers resuelve al contexto del sandbox. Los handlers no saben nada, y no tienen que saberlo.
/// </summary>
[ApiController]
[Route("api/sandbox")]
[Authorize]
[ServiceFilter(typeof(EnterSandboxFilter))]
public sealed class SandboxController(
    IMediator mediator,
    ISandboxReset reset,
    ISandboxExperiments experiments) : ControllerBase
{
    /// <summary>
    /// Qué existe ya. De aquí sale el estado de cada paso del wizard: se deriva del dato, no de una
    /// bandera de progreso que se desincroniza en cuanto alguien resetea.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
        => Ok(await mediator.Send(new GetSandboxStatusQuery(), ct));

    // ── El ciclo, en el orden en que el wizard lo recorre ────────────────────────────────────────

    [HttpPost("plans")]
    public Task<IActionResult> CreatePlan(CreatePlanCommand command, CancellationToken ct) => Send(command, ct);

    [HttpPost("plans/rules")]
    public Task<IActionResult> AddRule(AddRuleToPlanCommand command, CancellationToken ct) => Send(command, ct);

    /// <summary>
    /// Lo que pagaría la regla que está en pantalla, paso a paso: el simulador de la pantalla real de
    /// reglas, sobre el plan de práctica.
    ///
    /// ★ LA MISMA CONSULTA QUE <c>PlanRulesController.Simulate</c>, Y LA MISMA FORMA DE RESPUESTA. No
    /// pasa por <see cref="Send{TResponse}"/> porque ése devuelve el <c>Result</c> envuelto, y el
    /// simulador compartido lee el DTO tal cual lo manda el endpoint real. El filtro de la clase ya
    /// entró al sandbox, así que el plan se busca en el esquema de práctica.
    /// </summary>
    [HttpPost("plans/{planId:guid}/rules/simulate")]
    public async Task<IActionResult> SimulateRule(Guid planId, SimulateRuleQuery query, CancellationToken ct)
    {
        if (query.PlanId != planId)
        {
            return BadRequest(new { message = "Route planId does not match body planId." });
        }

        var result = await mediator.Send(query, ct);
        return result.IsSuccess ? Ok(result.Value) : UnprocessableEntity(new { message = result.Error });
    }

    [HttpPost("payees")]
    public Task<IActionResult> CreatePayee(CreatePayeeCommand command, CancellationToken ct) => Send(command, ct);

    [HttpPost("quotas")]
    public Task<IActionResult> CreateQuota(CreateQuotaCommand command, CancellationToken ct) => Send(command, ct);

    [HttpPost("assignments")]
    public Task<IActionResult> Assign(AssignPlanToPayeeCommand command, CancellationToken ct) => Send(command, ct);

    [HttpPost("transactions")]
    public Task<IActionResult> Ingest(IngestTransactionCommand command, CancellationToken ct) => Send(command, ct);

    /// <summary>
    /// El paso «calcular» del recorrido. Ver <see cref="CalculateSandboxPendingCommand"/>: el camino
    /// normal encola un trabajo en segundo plano que corre con el contexto REAL, así que aquí se llama
    /// al mismo motor de asignación en el acto.
    /// </summary>
    [HttpPost("transactions/process")]
    public Task<IActionResult> Process(CancellationToken ct) => Send(new CalculateSandboxPendingCommand(), ct);

    [HttpPost("pay-runs")]
    public Task<IActionResult> CalculatePayRun(CalculatePayRunCommand command, CancellationToken ct) => Send(command, ct);

    [HttpPost("pay-runs/approve")]
    public Task<IActionResult> ApprovePayRun(ApprovePayRunCommand command, CancellationToken ct) => Send(command, ct);

    [HttpPost("pay-runs/pay")]
    public Task<IActionResult> MarkPaid(MarkPayRunPaidCommand command, CancellationToken ct) => Send(command, ct);

    /// <summary>
    /// Borra todo lo que el usuario haya creado practicando, y le deja el ciclo entero por delante otra vez.
    ///
    /// ★ ES BARATO PORQUE LOS DATOS ESTÁN APARTE. Con una bandera sobre las tablas reales, «borrar lo
    /// de práctica» sería una consulta de borrado sobre las tablas del dinero — la operación más
    /// peligrosa del sistema, corriendo a petición del usuario. Aquí no toca nada real ni por error.
    /// </summary>
    [HttpPost("reset")]
    public async Task<IActionResult> Reset(CancellationToken ct)
    {
        var deleted = await reset.ResetAsync(ct);
        return Ok(new { deleted });
    }

    // ── Historial de experimentos ─────────────────────────────────────────────────────

    [HttpGet("experiments")]
    public async Task<IActionResult> ListExperiments(CancellationToken ct)
        => Ok(await experiments.ListAsync(ct));

    [HttpGet("experiments/{id:guid}")]
    public async Task<IActionResult> GetExperiment(Guid id, CancellationToken ct)
    {
        var found = await experiments.GetAsync(id, ct);
        return found is null ? NotFound() : Ok(found);
    }

    /// <summary>Guarda el recorrido actual como experimento, o vuelve a guardar sobre uno existente.</summary>
    [HttpPost("experiments")]
    public async Task<IActionResult> SaveExperiment(SaveExperimentRequest request, CancellationToken ct)
        => Ok(await experiments.SaveAsync(request.Id, request.Name, request.Snapshot, ct));

    [HttpDelete("experiments/{id:guid}")]
    public async Task<IActionResult> DeleteExperiment(Guid id, CancellationToken ct)
        => await experiments.DeleteAsync(id, ct) ? NoContent() : NotFound();

    private async Task<IActionResult> Send<TResponse>(IRequest<TResponse> command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);

        // El contrato de respuesta es el mismo que el de las pantallas reales: los `Result<T>` traen su
        // propio error, y el resto viaja tal cual.
        return result switch
        {
            Wasnie.Domain.Common.Results.Result r when !r.IsSuccess => BadRequest(new { message = r.Error }),
            _ => Ok(result),
        };
    }
}

/// <param name="Id">Vacío para crear uno nuevo; con valor, para volver a guardar sobre el mismo.</param>
public sealed record SaveExperimentRequest(Guid? Id, string Name, string Snapshot);

/// <summary>
/// Marca la petición como del onboarding antes de que la acción se ejecute.
///
/// ★★ ANTES DE LA ACCIÓN, NO DENTRO. Si cada método tuviera que acordarse de entrar al sandbox, el
/// día que alguien añada el paso once se olvidaría, y ese paso escribiría en las tablas reales sin
/// que nada fallara. Puesto en el filtro, la garantía es de la ruta entera: todo lo que cuelgue de
/// `api/sandbox` es práctica, por construcción.
/// </summary>
public sealed class EnterSandboxFilter(ISandboxScope scope) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context) => scope.Enter();

    public void OnActionExecuted(ActionExecutedContext context) { }
}
