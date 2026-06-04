using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.IntegrationTests.TestDoubles;

/// <summary>
/// Test double for IQuotaAttainmentService.
/// Returns a fixed attainment value for all calls (default: Zero).
/// Used in tests that exercise Flat/Tiered plans where attainment is irrelevant.
/// </summary>
public sealed class StubQuotaAttainmentService : IQuotaAttainmentService
{
    private readonly AttainmentPercentage _value;

    public StubQuotaAttainmentService(AttainmentPercentage? value = null)
    {
        _value = value ?? AttainmentPercentage.Zero;
    }

    public int CallCount { get; private set; }

    public Task<AttainmentPercentage> ComputeAsync(
        Guid payeeId, Guid planId, DateOnly asOfDate, CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult(_value);
    }
}
