using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Common;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;
using CompensationPlanStatus = Wasnie.Domain.Compensation.Plans.PlanStatus;

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
public sealed record CalculateSandboxPendingCommand : IRequest<Result<SandboxCalculationDto>>;

/// <param name="CreditsCreated">Los créditos que generó este cálculo.</param>
/// <param name="Uncredited">Las ventas que quedaron sin comisión, cada una con su motivo.</param>
public sealed record SandboxCalculationDto(
    int CreditsCreated,
    IReadOnlyList<SandboxUncreditedSaleDto> Uncredited);

/// <param name="Reason">Un código de <see cref="SandboxUncreditedReason"/>, nunca una frase (§C1).</param>
/// <param name="RuleNames">Las reglas implicadas, cuando el motivo es de una regla (el disparador).</param>
public sealed record SandboxUncreditedSaleDto(
    string ReferenceNumber,
    string Reason,
    IReadOnlyList<string> RuleNames);

/// <summary>Por qué una venta calculada no generó comisión. Códigos, no prosa: la pantalla los traduce.</summary>
public static class SandboxUncreditedReason
{
    public const string NoPayee = UnprocessablePendingSpec.NoPayeeReason;
    public const string NoActiveAssignment = UnprocessablePendingSpec.NoActiveAssignmentReason;
    public const string CurrencyMismatch = UnprocessablePendingSpec.CurrencyMismatchReason;
    public const string NoApplicableRules = "NoApplicableRules";
    public const string TriggerNotMatched = "TriggerNotMatched";

    /// <summary>El motor no generó crédito y ninguno de los motivos conocidos lo explica.</summary>
    public const string NotCredited = "NotCredited";
}

public sealed class CalculateSandboxPendingHandler(
    IApplicationDbContext db,
    ICreditAllocationService creditAllocationService,
    IRuleCalculationExplainer explainer,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid)
    : IRequestHandler<CalculateSandboxPendingCommand, Result<SandboxCalculationDto>>
{
    public async Task<Result<SandboxCalculationDto>> Handle(
        CalculateSandboxPendingCommand request, CancellationToken cancellationToken)
    {
        var pending = await db.CompensationTransactions
            .Where(t => t.Status == CompensationTransactionStatus.Pending)
            .ToListAsync(cancellationToken);

        var created = 0;
        var uncredited = new List<CompensationTransaction>();

        foreach (var tx in pending)
        {
            var credits = await creditAllocationService.AllocateAsync(tx, cancellationToken);
            if (credits.Count == 0)
            {
                uncredited.Add(tx);
                continue;
            }

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

        // ★★ CERO CRÉDITOS NO ES UN ERROR, PERO TAMPOCO PUEDE SER SILENCIO (§B1). Antes el `continue` de
        // arriba era el final de la historia: la venta quedaba pendiente, el paso decía «hecho» sin
        // decir nada, y el usuario que había puesto un disparador que la venta no cumplía no tenía forma
        // de saberlo. Cada venta sin comisión vuelve ahora con su motivo.
        var explained = new List<SandboxUncreditedSaleDto>(uncredited.Count);
        foreach (var tx in uncredited)
        {
            explained.Add(await ExplainAsync(tx, cancellationToken));
        }

        return Result<SandboxCalculationDto>.Success(new SandboxCalculationDto(created, explained));
    }

    /// <summary>
    /// Por qué el motor no generó crédito para una venta, en el orden en que el motor lo decide.
    /// </summary>
    /// <remarks>
    /// ★★ NO ES UN SEGUNDO MOTOR, ES UNA LECTURA DEL PRIMERO. Cada comprobación usa la pieza que usa el
    /// motor: <see cref="PlanAssignmentResolver.Candidates"/> para las asignaciones (la regla de
    /// elegibilidad única), y <see cref="IRuleCalculationExplainer"/> —el mismo cálculo que el pay run—
    /// para saber si el disparador de cada regla se cumplió. Lo único que se repite es el filtro de
    /// vigencia de las reglas (Decision #41, `CreditAllocationService.BuildCreditsAsync`), porque allí es
    /// un detalle privado; si ese filtro cambia, este diagnóstico tiene que cambiar con él.
    /// </remarks>
    private async Task<SandboxUncreditedSaleDto> ExplainAsync(
        CompensationTransaction tx, CancellationToken cancellationToken)
    {
        SandboxUncreditedSaleDto Because(string reason, IReadOnlyList<string>? rules = null) =>
            new(tx.ReferenceNumber, reason, rules ?? []);

        if (tx.PayeeId is not { } payeeId)
        {
            return Because(SandboxUncreditedReason.NoPayee);
        }

        var date = tx.TransactionDate;
        var currency = tx.Amount.Currency;

        var payeeAssignments = await db.PlanAssignments
            .Where(a => a.PayeeId == payeeId)
            .ToListAsync(cancellationToken);

        var planIds = payeeAssignments.Select(a => a.PlanId).Distinct().ToList();
        var plans = await db.CompensationPlans
            .Include(p => p.Rules)
            .Where(p => planIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var planCurrencyById = plans.ToDictionary(p => p.Id, p => p.Currency);
        var archivedPlanIds = plans
            .Where(p => p.Status == CompensationPlanStatus.Archived)
            .Select(p => p.Id)
            .ToHashSet();

        var candidates = PlanAssignmentResolver.Candidates(
            payeeAssignments, date, currency, planCurrencyById, archivedPlanIds);

        if (candidates.Count == 0)
        {
            // Mismo reparto que UnprocessablePendingSpec: si ni siquiera hay una asignación activa que
            // cubra la fecha, es eso; si la hay pero ningún plan está en la moneda de la venta, es la
            // moneda. Lo demás (un plan archivado) cae en el genérico.
            var covering = payeeAssignments.Where(a =>
                a.Status == AssignmentStatus.Active
                && a.EffectivePeriod.Start <= date
                && a.EffectivePeriod.End >= date).ToList();

            if (covering.Count == 0)
            {
                return Because(SandboxUncreditedReason.NoActiveAssignment);
            }

            var anyInCurrency = covering.Any(a =>
                planCurrencyById.TryGetValue(a.PlanId, out var planCurrency)
                && string.Equals(planCurrency, currency, StringComparison.OrdinalIgnoreCase));

            return Because(anyInCurrency
                ? SandboxUncreditedReason.NotCredited
                : SandboxUncreditedReason.CurrencyMismatch);
        }

        var planById = plans.ToDictionary(p => p.Id);
        var applicable = candidates
            .Select(a => planById[a.PlanId])
            .SelectMany(plan => plan.Rules
                // Decision #41 — el mismo filtro que CreditAllocationService.BuildCreditsAsync.
                .Where(r => r.IsActive &&
                    (r.EffectivePeriod is null ||
                     (r.EffectivePeriod.Start <= date && r.EffectivePeriod.End >= date)))
                .Select(rule => (Plan: plan, Rule: rule)))
            .ToList();

        if (applicable.Count == 0)
        {
            return Because(SandboxUncreditedReason.NoApplicableRules);
        }

        var notMatched = applicable
            .Where(x => explainer.Explain(x.Rule, tx, x.Plan.Currency).Steps.Any(s =>
                s.Component == RuleCalculationComponent.Trigger
                && s.Outcome == RuleCalculationOutcome.NotMatched))
            .Select(x => x.Rule.Name)
            .ToList();

        // Sólo se culpa al disparador si es la razón de TODAS las reglas: si alguna lo cumplió y aun así
        // no hubo crédito, la causa es otra y decir «el disparador» sería mentir.
        return notMatched.Count == applicable.Count
            ? Because(SandboxUncreditedReason.TriggerNotMatched, notMatched)
            : Because(SandboxUncreditedReason.NotCredited);
    }
}
