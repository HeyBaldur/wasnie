using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Sandbox;

/// <summary>
/// Calcula, en el acto, las ventas pendientes del recorrido guiado.
/// </summary>
/// <remarks>
/// ★★ EXISTE PORQUE EL CAMINO NORMAL ES ASÍNCRONO, Y ESO AQUÍ NO SIRVE POR DOS MOTIVOS. En el
/// producto, «procesar pendientes» encola un trabajo en segundo plano: el usuario recibe un id de
/// trabajo y el cálculo ocurre después. Ese trabajo (a) no devuelve nada que enseñar en el momento,
/// que es justo lo que el paso tiene que mostrar, y (b) —lo importante— corre con el contexto REAL,
/// porque los trabajos en segundo plano piden el contexto concreto y no la interfaz conmutable. Un
/// recorrido de práctica que encolara ese trabajo pondría al motor a procesar las ventas de verdad
/// de la empresa.
///
/// ★★ Y AUN ASÍ NO DUPLICA NADA. La cuenta la hace `ICreditAllocationService`, el mismo servicio que
/// usa la ingesta inmediata y el trabajo en segundo plano; aquí sólo se le pasan las transacciones y
/// se guarda el resultado, con la misma secuencia que el camino inmediato de la ingesta: asignar,
/// marcar calculada con el total, guardar. Si mañana cambia cómo se calcula una comisión, este paso
/// cambia con ella.
/// </remarks>
public sealed record CalculateSandboxPendingCommand : IRequest<Result<int>>;

public sealed class CalculateSandboxPendingHandler(
    IApplicationDbContext db,
    ICreditAllocationService creditAllocationService,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid)
    : IRequestHandler<CalculateSandboxPendingCommand, Result<int>>
{
    public async Task<Result<int>> Handle(CalculateSandboxPendingCommand request, CancellationToken cancellationToken)
    {
        var pending = await db.CompensationTransactions
            .Where(t => t.Status == CompensationTransactionStatus.Pending)
            .ToListAsync(cancellationToken);

        var created = 0;

        foreach (var tx in pending)
        {
            var credits = await creditAllocationService.AllocateAsync(tx, cancellationToken);
            if (credits.Count == 0) continue;

            foreach (var credit in credits)
                db.Credits.Add(credit);

            var total = credits.Skip(1).Aggregate(credits[0].CreditedAmount, (acc, c) => acc.Add(c.CreditedAmount));
            tx.MarkCalculated(credits.Count, total, currentUser.UserId ?? "system", clock.UtcNowOffset, guid.NewGuid());
            created += credits.Count;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        // Cero no es un fallo: significa que el motor no encontró por dónde pagar esa venta, y el paso
        // siguiente seguirá bloqueado con su motivo. Decir «error» aquí sería culpar al usuario de una
        // configuración incompleta que la pantalla ya explica.
        return Result<int>.Success(created);
    }
}
