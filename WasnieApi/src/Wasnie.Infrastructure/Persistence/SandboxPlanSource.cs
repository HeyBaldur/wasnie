using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;

namespace Wasnie.Infrastructure.Persistence;

/// <summary>La configuración armada practicando. Ver <see cref="ISandboxPlanSource"/>.</summary>
public sealed class SandboxPlanSource(SandboxDbContext db) : ISandboxPlanSource
{
    public async Task<SandboxPlanConfiguration?> GetCurrentPlanAsync(CancellationToken cancellationToken)
    {
        // El último que armó: el recorrido va de uno en uno y, si probó varias veces, lo que quiere
        // llevarse es lo último que le cerró. Es el mismo criterio con el que la pantalla se pinta.
        //
        // ★ ENTIDADES ENTERAS, NO UNA PROYECCIÓN. La tabla de tasas es un tipo propiedad; sacarla
        // suelta la deja sin dueño y EF se niega en ejecución. Además hace falta completa: promover
        // media escalera pagaría distinto a lo que el usuario probó.
        var plan = await db.CompensationPlans
            .AsNoTracking()
            .Include(p => p.Rules)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (plan is null)
        {
            return null;
        }

        var rules = plan.Rules
            // ★ LO APAGADO NO CRUZA. Una regla que el usuario detuvo o desactivó mientras probaba es
            // una que decidió que NO paga; llevarla al plan real activa una decisión que él ya tomó
            // al revés, y allí sí hay dinero detrás.
            .Where(r => r.IsActive && !r.IsStopped)
            .OrderBy(r => r.SortOrder)
            .Select(r => new SandboxRuleConfiguration(
                r.Name,
                r.SortOrder,
                r.Measurement,
                r.RateTable,
                r.Trigger,
                r.Modifier,
                r.Cap,
                r.Floor))
            .ToList();

        return new SandboxPlanConfiguration(
            plan.Id,
            plan.Name,
            plan.Description,
            plan.EffectivePeriod.Start,
            plan.EffectivePeriod.End,
            plan.Currency,
            rules);
    }
}
