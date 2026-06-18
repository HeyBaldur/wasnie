using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.IntegrationTests.TestDoubles;

/// <summary>
/// Test double for IQuotaAttainmentService.
/// Returns fixed attainment / split-context values for all calls.
/// Used in tests that exercise Flat/Tiered plans where attainment is irrelevant,
/// and optionally in split-at-quota tests via the splitContext constructor parameter.
/// </summary>
public sealed class StubQuotaAttainmentService : IQuotaAttainmentService
{
    private readonly AttainmentPercentage _value;
    private readonly AttainmentSplitContext? _splitContext;

    public StubQuotaAttainmentService(
        AttainmentPercentage? value = null,
        AttainmentSplitContext? splitContext = null)
    {
        _value = value ?? AttainmentPercentage.Zero;
        _splitContext = splitContext;
    }

    public int CallCount { get; private set; }

    public Task<AttainmentPercentage> ComputeAsync(
        Guid payeeId, Guid planId, DateOnly asOfDate, CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult(_value);
    }

    public Task<AttainmentSplitContext?> GetSplitContextAsync(
        Guid payeeId, Guid planId, DateOnly asOfDate, CancellationToken ct = default)
    {
        return Task.FromResult(_splitContext);
    }
}
