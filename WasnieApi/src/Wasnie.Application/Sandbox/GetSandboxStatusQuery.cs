using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Application.Sandbox;

/// <summary>
/// Qué existe ya en el recorrido guiado de esta empresa, con el detalle suficiente para ENSEÑARLO.
/// </summary>
/// <remarks>
/// ★★ EL PROGRESO SE DERIVA, NO SE GUARDA. No hay una columna «paso actual»: se mira lo que hay. Una
/// bandera de progreso se desincroniza en cuanto el usuario resetea, o crea algo por otro camino, y
/// entonces el recorrido promete un paso que ya no tiene sus piezas o bloquea uno que sí las tiene
/// (§B5: lo que se puede calcular del dato, se calcula).
///
/// ★★ Y DEVUELVE IMPORTES, NO SÓLO CONTADORES. Un recorrido que responde «hecho ✓» no enseña nada: el
/// usuario está aquí para VER que una venta de 10.000 al 5 % produce 500, y que ese 500 acaba en un
/// payout aprobado y pagado. Por eso viajan la venta, el crédito con su cuenta y el payout con su
/// estado — son los datos reales del esquema Sandbox, leídos tal cual.
/// </remarks>
public sealed record GetSandboxStatusQuery : IRequest<SandboxStatusDto>;

/// <param name="Rate">
/// La tasa plana TAL COMO ESTÁ GUARDADA: un multiplicador (0.05 es un 5 %) o, midiendo por unidades,
/// un importe por unidad. No se convierte aquí; la pantalla la escribe con <c>rate-format.ts</c>, que es
/// quien sabe qué significa según la medición.
/// </param>
/// <remarks>
/// ★ EL RESUMEN DE LO QUE SE CONFIGURÓ, NO SÓLO SU NOMBRE. La regla del recorrido tiene ahora todas las
/// opciones de la pantalla real; enseñar después sólo «Flat» dejaría al usuario sin ver si el tope, el
/// modificador o el disparador que acaba de poner quedaron guardados.
/// </remarks>
public sealed record SandboxRuleDto(
    Guid Id,
    string Name,
    string TableType,
    decimal? Rate,
    string MeasurementType,
    int TierCount,
    int ConditionCount,
    decimal? ModifierFactor,
    decimal? CapAmount,
    decimal? FloorAmount);

/// <param name="BaseAmount">El importe de la venta sobre el que se calculó: la mitad izquierda de la cuenta.</param>
/// <param name="CreditedAmount">La comisión resultante: la mitad derecha.</param>
public sealed record SandboxCreditDto(
    Guid Id,
    string PayeeName,
    string RuleName,
    decimal BaseAmount,
    decimal CreditedAmount,
    string Currency,
    bool Consumed);

public sealed record SandboxPayoutDto(
    Guid Id,
    string PayeeName,
    decimal Total,
    string Currency,
    string Status,
    int LineCount);

public sealed record SandboxPlanDto(
    Guid Id,
    string Name,
    string Currency,
    DateOnly EffectiveStart,
    DateOnly EffectiveEnd,
    int RuleCount);

public sealed record SandboxPayeeDto(Guid Id, string FullName, string EmployeeCode);

public sealed record SandboxQuotaDto(Guid Id, decimal Amount, string Currency, string MeasurementType);

public sealed record SandboxAssignmentDto(Guid Id, DateOnly EffectiveStart, DateOnly EffectiveEnd);

public sealed record SandboxTransactionDto(
    Guid Id,
    string ReferenceNumber,
    decimal Amount,
    string Currency,
    DateOnly TransactionDate,
    string Status);

public sealed record SandboxPayRunDto(
    Guid Id,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    int PayoutCount);

public sealed record SandboxStatusDto(
    SandboxPlanDto? Plan,
    SandboxRuleDto? Rule,
    SandboxPayeeDto? Payee,
    SandboxQuotaDto? Quota,
    SandboxAssignmentDto? Assignment,
    SandboxTransactionDto? Transaction,
    IReadOnlyList<SandboxCreditDto> Credits,
    SandboxPayRunDto? PayRun,
    IReadOnlyList<SandboxPayoutDto> Payouts);

public sealed class GetSandboxStatusHandler(IApplicationDbContext db)
    : IRequestHandler<GetSandboxStatusQuery, SandboxStatusDto>
{
    /// <remarks>
    /// ★ TODAS LAS CONSULTAS SIN SEGUIMIENTO, Y NO ES UNA OPTIMIZACIÓN. Estas proyecciones sacan tipos
    /// PROPIEDAD (el importe de una venta, la tabla de tasas de una regla) sin su dueño; EF se niega a
    /// seguir un tipo propiedad huérfano y la petición revienta en ejecución. Además es lo correcto:
    /// esto sólo lee para pintar.
    /// </remarks>
    public async Task<SandboxStatusDto> Handle(GetSandboxStatusQuery request, CancellationToken cancellationToken)
    {
        // El más reciente de cada cosa: el recorrido va de uno en uno, y si el usuario probó varias
        // veces, lo que importa para seguir (y para enseñar) es lo último que hizo.
        var plan = await db.CompensationPlans
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.Id, p.Name, p.Currency, Start = p.EffectivePeriod.Start, End = p.EffectivePeriod.End,
                RuleCount = p.Rules.Count,
                Rule = p.Rules
                    .OrderByDescending(r => r.SortOrder)
                    // Columnas JSON con conversión, igual que la tabla: se materializan enteras y el
                    // resumen se saca en memoria, abajo.
                    .Select(r => new { r.Id, r.Name, r.RateTable, r.Measurement, r.Trigger, r.Modifier, r.Cap, r.Floor })
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var payee = await db.Payees
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.Id, p.FullName, p.EmployeeCode })
            .FirstOrDefaultAsync(cancellationToken);

        var quota = await db.Quotas
            .AsNoTracking()
            .OrderByDescending(q => q.CreatedAt)
            .Select(q => new { q.Id, q.Amount, q.MeasurementType })
            .FirstOrDefaultAsync(cancellationToken);

        var assignment = await db.PlanAssignments
            .AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new { a.Id, Start = a.EffectivePeriod.Start, End = a.EffectivePeriod.End })
            .FirstOrDefaultAsync(cancellationToken);

        var transaction = await db.CompensationTransactions
            .AsNoTracking()
            .OrderByDescending(t => t.IngestedAt)
            .Select(t => new { t.Id, t.ReferenceNumber, t.Amount, t.TransactionDate, t.Status })
            .FirstOrDefaultAsync(cancellationToken);

        var payRun = await db.PayRuns
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { r.Id, r.PeriodStart, r.PeriodEnd, r.Status })
            .FirstOrDefaultAsync(cancellationToken);

        var creditRows = await db.Credits
            .AsNoTracking()
            .OrderByDescending(c => c.AllocatedAt)
            .Take(10)
            .Select(c => new
            {
                c.Id, c.PayeeId, c.OriginalAmount, c.CreditedAmount, c.ConsumedAt,
                RuleName = c.RuleSnapshot.RuleName,
            })
            .ToListAsync(cancellationToken);

        var payoutRows = payRun is null
            ? []
            : await db.CompensationPayouts
            .AsNoTracking()
                .Where(p => p.PayRunId == payRun.Id)
                .Select(p => new { p.Id, p.PayeeId, p.TotalCommission, p.Status, LineCount = p.Lines.Count })
                .ToListAsync(cancellationToken);

        // Un único diccionario de nombres: los créditos y los payouts hablan de las mismas personas, y
        // pedir el nombre fila por fila serían N consultas para enseñar una tabla de tres líneas.
        var payeeIds = creditRows.Select(c => c.PayeeId)
            .Concat(payoutRows.Select(p => p.PayeeId))
            .Distinct()
            .ToList();

        var names = await db.Payees
            .AsNoTracking()
            .Where(p => payeeIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.FullName, cancellationToken);

        string NameOf(Guid id) => names.TryGetValue(id, out var name) ? name : string.Empty;

        return new SandboxStatusDto(
            Plan: plan is null ? null : new SandboxPlanDto(
                plan.Id, plan.Name, plan.Currency, plan.Start, plan.End, plan.RuleCount),
            Rule: plan?.Rule is null ? null : new SandboxRuleDto(
                plan.Rule.Id,
                plan.Rule.Name,
                plan.Rule.RateTable.Type.ToString(),
                plan.Rule.RateTable.FlatRate,
                plan.Rule.Measurement.Type.ToString(),
                (plan.Rule.RateTable.Tiers?.Count ?? 0) + (plan.Rule.RateTable.AttainmentTiers?.Count ?? 0),
                // Sin condiciones el disparador es «siempre»: cero aquí es exactamente eso.
                plan.Rule.Trigger?.Conditions.Count ?? 0,
                plan.Rule.Modifier?.Factor,
                plan.Rule.Cap?.Amount.Amount,
                plan.Rule.Floor?.Amount.Amount),
            Payee: payee is null ? null : new SandboxPayeeDto(payee.Id, payee.FullName, payee.EmployeeCode),
            Quota: quota is null ? null : new SandboxQuotaDto(
                quota.Id, quota.Amount.Amount, quota.Amount.Currency, quota.MeasurementType.ToString()),
            Assignment: assignment is null ? null : new SandboxAssignmentDto(
                assignment.Id, assignment.Start, assignment.End),
            Transaction: transaction is null ? null : new SandboxTransactionDto(
                transaction.Id, transaction.ReferenceNumber, transaction.Amount.Amount,
                transaction.Amount.Currency, transaction.TransactionDate, transaction.Status.ToString()),
            Credits: creditRows.Select(c => new SandboxCreditDto(
                c.Id, NameOf(c.PayeeId), c.RuleName,
                c.OriginalAmount.Amount, c.CreditedAmount.Amount, c.CreditedAmount.Currency,
                c.ConsumedAt is not null)).ToList(),
            PayRun: payRun is null ? null : new SandboxPayRunDto(
                payRun.Id, payRun.PeriodStart, payRun.PeriodEnd, payRun.Status.ToString(), payoutRows.Count),
            Payouts: payoutRows.Select(p => new SandboxPayoutDto(
                p.Id, NameOf(p.PayeeId), p.TotalCommission.Amount, p.TotalCommission.Currency,
                p.Status.ToString(), p.LineCount)).ToList());
    }
}
