using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Sandbox;

namespace Wasnie.Api.Controllers;

/// <summary>
/// Promover a plan real la configuración que el usuario armó practicando.
/// </summary>
/// <remarks>
/// ★★ ESTÁ FUERA DE <see cref="SandboxController"/> A PROPÓSITO, Y NO ES UNA CUESTIÓN DE ORDEN. Aquel
/// lleva <c>EnterSandboxFilter</c> a nivel de clase: TODO lo que cuelga de él es práctica, por
/// construcción. Esta acción es la única del recorrido que escribe en las tablas del dinero real, así
/// que no puede vivir donde la garantía es la contraria. Por eso tampoco cuelga de la ruta
/// `api/sandbox`: si mañana alguien reagrupa los controladores por prefijo, que no se lleve éste por
/// delante.
///
/// ★ Y EL HANDLER LO VUELVE A COMPROBAR. Un comentario no impide que alguien añada el filtro aquí;
/// la guarda de <see cref="PromoteSandboxPlanHandler"/> revienta si esta acción llegara a correr
/// dentro del ámbito de práctica, en vez de crear un plan «real» en el esquema equivocado.
/// </remarks>
[ApiController]
[Route("api/plan-promotions")]
[Authorize]
public sealed class SandboxPromotionController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Crea un plan REAL, en borrador, con las reglas del recorrido. Los datos de prueba no cruzan.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Promote(CancellationToken ct)
    {
        var result = await mediator.Send(new PromoteSandboxPlanCommand(), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(new { message = result.Error });
    }
}
