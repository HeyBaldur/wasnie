using Wasnie.Application.Compensation.Enrichment;

namespace Wasnie.UnitTests.TestDoubles;

/// <summary>
/// Test double for <see cref="ITransactionEnrichmentService"/>. Returns a resolver built from an
/// in-memory list of mappings (empty by default), so a test can exercise enrichment without a DB, or
/// simply satisfy the constructor of an ingest call-site that does not care about categories.
/// </summary>
public sealed class FakeTransactionEnrichmentService : ITransactionEnrichmentService
{
    private readonly List<(string InputField, string InputValue, string Category)> _mappings = new();

    public FakeTransactionEnrichmentService Add(string inputField, string inputValue, string category)
    {
        _mappings.Add((inputField, inputValue, category));
        return this;
    }

    public Task<CategoryResolver> LoadResolverAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(CategoryResolver.FromMappings(_mappings));
}
