using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;

namespace Wasnie.Infrastructure.Persistence;

/// <summary>
/// El borrado de los datos de práctica. Ver <see cref="ISandboxReset"/>.
/// </summary>
/// <remarks>
/// ★★ EL ORDEN ES EL DE LAS DEPENDENCIAS, DE LA HOJA A LA RAÍZ. Borrar un plan antes que los créditos
/// que lo referencian no da un dato huérfano: da un error de clave foránea y un reseteo a medias, que
/// es peor que no haber empezado — el usuario se queda con medio ciclo y sin forma de repetirlo.
///
/// ★ `ExecuteDelete` NO PASA POR EL SEGUIMIENTO DE CAMBIOS, y aquí eso es lo que se quiere: es un
/// borrado masivo, no una operación de dominio. No hay eventos que despachar ni invariantes que
/// mantener — el sandbox entero deja de existir.
///
/// ★ EL FILTRO DE TENANT LO PONE EL PROPIO CONTEXTO. Los filtros de consulta del modelo se heredan,
/// así que estas sentencias sólo alcanzan las filas de la empresa actual: una empresa no puede
/// resetear la práctica de otra.
/// </remarks>
public sealed class SandboxReset(SandboxDbContext db) : ISandboxReset
{
    public async Task<int> ResetAsync(CancellationToken cancellationToken)
    {
        var deleted = 0;

        deleted += await db.PayRunSettlements.ExecuteDeleteAsync(cancellationToken);
        deleted += await db.PayeeLedgerEntries.ExecuteDeleteAsync(cancellationToken);
        deleted += await db.PayeeBalances.ExecuteDeleteAsync(cancellationToken);
        deleted += await db.ReconciliationClosures.ExecuteDeleteAsync(cancellationToken);
        deleted += await db.Credits.ExecuteDeleteAsync(cancellationToken);
        deleted += await db.CompensationPayouts.ExecuteDeleteAsync(cancellationToken);
        deleted += await db.PayRuns.ExecuteDeleteAsync(cancellationToken);
        deleted += await db.CompensationTransactions.ExecuteDeleteAsync(cancellationToken);
        deleted += await db.Quotas.ExecuteDeleteAsync(cancellationToken);
        deleted += await db.PlanAssignments.ExecuteDeleteAsync(cancellationToken);
        deleted += await db.CategoryMappings.ExecuteDeleteAsync(cancellationToken);
        deleted += await db.CompensationPlans.ExecuteDeleteAsync(cancellationToken);
        deleted += await db.Payees.ExecuteDeleteAsync(cancellationToken);

        return deleted;
    }
}
