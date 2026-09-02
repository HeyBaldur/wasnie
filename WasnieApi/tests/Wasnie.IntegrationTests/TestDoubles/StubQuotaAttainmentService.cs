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

    /// <param name="source">
    /// ★★ THE DEFAULT FOLLOWS THE VALUE, AND IT DID NOT USED TO — which let this stub describe a
    /// state the production service can never produce. The parameter defaulted to NoTarget outright,
    /// so a caller that supplied a real 75% and said nothing about the source got "75% attainment,
    /// measured against no target": a contradiction, since a ratio can only be 75% BECAUSE a target
    /// existed to be 75% of.
    ///
    /// It was harmless while the bracket path ignored the source, and stopped being harmless the
    /// moment the engine started refusing to pay on NoTarget — the refusal was correct and the
    /// fixture was wrong. Now: no value means NoTarget (a zero nobody measured), a value means
    /// Measured, and a caller who wants an unusual pairing has to ask for it in writing.
    /// </param>
    public StubQuotaAttainmentService(
        AttainmentPercentage? value = null,
        AttainmentSplitContext? splitContext = null,
        AttainmentSource? source = null)
    {
        _value = value ?? AttainmentPercentage.Zero;
        _source = source ?? (value is null ? AttainmentSource.NoTarget : AttainmentSource.Measured);
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
