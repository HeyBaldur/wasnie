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
    private readonly AttainmentSource _source;
    private readonly AttainmentSplitContext? _splitContext;

    public StubQuotaAttainmentService(
        AttainmentPercentage? value = null,
        AttainmentSplitContext? splitContext = null,
        // Defaults to NoTarget because the default VALUE is zero, and a stub that reported a zero as
        // Measured would be asserting the one thing the production service now refuses to say.
        AttainmentSource source = AttainmentSource.NoTarget)
    {
        _value = value ?? AttainmentPercentage.Zero;
        _source = value is null ? AttainmentSource.NoTarget : source;
        _splitContext = splitContext;
    }

    public int CallCount { get; private set; }

    public Task<AttainmentReading> ComputeAsync(
        Guid payeeId, Guid planId, DateOnly asOfDate, CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult(new AttainmentReading(_value, _source));
    }

    public Task<AttainmentSplitContext?> GetSplitContextAsync(
        Guid payeeId, Guid planId, DateOnly asOfDate, CancellationToken ct = default)
    {
        return Task.FromResult(_splitContext);
    }
}
