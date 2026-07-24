using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Plans;

/// <summary>
/// Distinct categories for the current tenant, unioned from two sources so the rule builder's picker is
/// complete:
///   (a) categories that actually reached transactions (CRM sync + lookup output) — the real values a
///       rule must match, and the ones the mapping table does NOT contain when they come straight from
///       the CRM;
///   (b) categories declared in the lookup table, which may exist before any sync populated a row.
/// Both DbSets carry the tenant global query filter, so no explicit tenant predicate is needed. Two
/// simple projections, deduped case-insensitively (the engine compares OrdinalIgnoreCase) — no N+1.
/// </summary>
public sealed class GetCategoryValuesHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetCategoryValuesQuery, Result<IReadOnlyList<string>>>
{
    public async Task<Result<IReadOnlyList<string>>> Handle(
        GetCategoryValuesQuery request, CancellationToken cancellationToken)
    {
        // Same audience as the trigger-field catalog: reading this is part of authoring a plan's rules.
        await authorizationService.RequireAsync(Permission.PlansRead, cancellationToken);

        var fromTransactions = await db.CompensationTransactions
            .Where(t => t.Category != null)
            .Select(t => t.Category!)
            .Distinct()
            .ToListAsync(cancellationToken);

        var fromMappings = await db.CategoryMappings
            .Select(m => m.Category)
            .Distinct()
            .ToListAsync(cancellationToken);

        // SortedSet with the same comparer the engine uses keeps one canonical casing per value and a
        // stable order. Transaction values are added first so, on a case tie, the exact form that
        // actually arrived is the one offered.
        var values = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in fromTransactions) values.Add(c);
        foreach (var c in fromMappings) values.Add(c);

        return Result<IReadOnlyList<string>>.Success(values.ToList());
    }
}
