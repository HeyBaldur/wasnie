namespace Wasnie.Application.Compensation.Enrichment;

/// <summary>
/// The enrichment phase of the pipeline: sits between ingest and calculation and derives a stable,
/// discrete <c>Category</c> for a transaction from the tenant's lookup table. Shared by all three ingest
/// origins (HubSpot / Excel / manual), exactly like <c>ITransactionCreateGuard</c>, so the rule is
/// written once and only invoked.
///
/// The contract is deliberately "load once, resolve many": callers that ingest in batches load a single
/// <see cref="CategoryResolver"/> up front and resolve every row in memory (no per-transaction query).
/// </summary>
public interface ITransactionEnrichmentService
{
    /// <summary>
    /// Loads the tenant's category mappings into an in-memory <see cref="CategoryResolver"/>. One query
    /// per batch — call it once before the ingest loop, then reuse the resolver for every row.
    /// </summary>
    Task<CategoryResolver> LoadResolverAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
