using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Assignments;

namespace Wasnie.Application.Compensation.Common;

/// <summary>
/// Single source of truth for "which plans could this transaction be credited to".
///
/// Loads a payee's assignments and hands them to <see cref="PlanAssignmentResolver.Candidates"/> — the
/// engine's own eligibility rule — so the options offered to the admin are EXACTLY the ones the engine
/// would consider. Both the plan selector on the manual form and the server-side validation of the
/// admin's choice go through here; if this ever diverged from the resolver, the UI would start offering
/// attributions the engine refuses (or hiding ones it would pick).
/// </summary>
public static class PayeePlanCandidates
{
    /// <param name="txDate">The transaction date — assignment periods are evaluated against it.</param>
    /// <param name="txCurrency">The transaction currency — plans in other currencies are not candidates.</param>
    public static async Task<IReadOnlyList<PlanAssignment>> LoadAsync(
        IApplicationDbContext db,
        Guid tenantId,
        Guid payeeId,
        DateOnly txDate,
        string txCurrency,
        CancellationToken ct = default)
    {
        // Mirrors CreditAllocationService: load all of the payee's assignments, then filter in memory.
        // EF Core 8 + SQL Server does not reliably translate DateOnly comparisons on the owned
        // DateRange properties, and a payee has very few assignments.
        var allPayeeAssignments = await db.PlanAssignments
            .IgnoreQueryFilters()
            .Where(pa => pa.TenantId == tenantId && pa.PayeeId == payeeId)
            .ToListAsync(ct);

        if (allPayeeAssignments.Count == 0)
            return [];

        var planIds = allPayeeAssignments.Select(a => a.PlanId).Distinct().ToList();
        var planCurrencyById = (await db.CompensationPlans
                .IgnoreQueryFilters()
                .Where(p => p.TenantId == tenantId && planIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Currency })
                .ToListAsync(ct))
            .ToDictionary(p => p.Id, p => p.Currency);

        return PlanAssignmentResolver.Candidates(
            allPayeeAssignments, txDate, txCurrency, planCurrencyById);
    }
}
