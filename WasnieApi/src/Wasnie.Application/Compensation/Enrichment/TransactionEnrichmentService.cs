using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Application.Compensation.Enrichment;

/// <inheritdoc cref="ITransactionEnrichmentService"/>
public sealed class TransactionEnrichmentService(IApplicationDbContext db) : ITransactionEnrichmentService
{
    public async Task<CategoryResolver> LoadResolverAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters + explicit TenantId: robust whether invoked under an HTTP request (manual
        // ingest) or a background job scope (HubSpot/Excel), mirroring CreditAllocationService's pattern.
        // One bounded query for the whole batch — the mapping table is tenant-sized config, not fact data.
        var rows = await db.CategoryMappings
            .IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId)
            .Select(m => new { m.InputField, m.InputValue, m.Category })
            .ToListAsync(cancellationToken);

        return CategoryResolver.FromMappings(
            rows.Select(r => (r.InputField, r.InputValue, r.Category)));
    }
}
