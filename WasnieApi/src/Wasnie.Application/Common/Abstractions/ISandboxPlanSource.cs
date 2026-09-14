using Wasnie.Domain.Compensation.Rules;

namespace Wasnie.Application.Common.Abstractions;

/// <summary>Una regla del recorrido, tal cual está guardada, lista para volver a crearse de verdad.</summary>
public sealed record SandboxRuleConfiguration(
    string Name,
    int SortOrder,
    Measurement Measurement,
    RateTable RateTable,
    Trigger? Trigger,
    Modifier? Modifier,
    Cap? Cap,
    Floor? Floor);

/// <param name="Rules">Sólo las que pagarían: una regla apagada o detenida en la práctica no cruza.</param>
public sealed record SandboxPlanConfiguration(
    Guid Id,
    string Name,
    string Description,
    DateOnly EffectiveStart,
    DateOnly EffectiveEnd,
    string Currency,
    IReadOnlyList<SandboxRuleConfiguration> Rules);

/// <summary>
/// Lee la CONFIGURACIÓN que el usuario dejó armada practicando: el plan y sus reglas, nada más.
/// </summary>
/// <remarks>
/// ★★ SÓLO LEE, Y ESE ES EL PUNTO. Es la única pieza que mira el esquema de práctica desde un camino
/// que después escribe en el real, así que no puede tener un método que escriba: si el cruce entre
/// esquemas cabe en «leer aquí, escribir allá», el error de mandar una escritura al lado equivocado
/// no existe como posibilidad.
///
/// ★★ CRUZA LA CONFIGURACIÓN, NO LOS DATOS DE PRUEBA. El payee inventado, la cuota, la venta de
/// 10.000 y sus créditos se quedan donde están. Lo que el usuario descubrió probando es la FORMA de
/// pagar; las cifras con las que la probó son de mentira y en el esquema real serían dinero que
/// nadie vendió.
///
/// ★ HABLA SIEMPRE CON EL CONTEXTO DEL SANDBOX, como el reseteo y el historial. Nunca con la interfaz
/// conmutable: no existe la variante «se ejecutó fuera del ámbito y leyó los planes reales».
/// </remarks>
public interface ISandboxPlanSource
{
    /// <summary>El plan que el usuario tiene armado ahora mismo en el recorrido, o nada si no hay.</summary>
    Task<SandboxPlanConfiguration?> GetCurrentPlanAsync(CancellationToken cancellationToken);
}
