using Wasnie.Application.Common.Interfaces;

namespace Wasnie.UnitTests.TestDoubles;

/// <summary>
/// Which plans a test's reader may open (KAN-93, bug 6).
///
/// ★ THE EXISTING PLAN TESTS USE <see cref="SeesEverything"/>, and that is the honest substitution:
/// they were all written for an administrator, whose real guard returns true for every plan because
/// they hold <c>Plans.Read</c>. Introducing the guard must not quietly change what those tests are
/// about — the narrowing has its own tests against the REAL <c>PlanAccessGuard</c>, where it belongs.
/// </summary>
internal sealed class FakePlanAccessGuard(IReadOnlySet<Guid>? allowed) : IPlanAccessGuard
{
    /// <summary>An administrator: every plan in the workspace.</summary>
    public static FakePlanAccessGuard SeesEverything() => new(null);

    public static FakePlanAccessGuard Sees(params Guid[] planIds) => new(planIds.ToHashSet());

    public static FakePlanAccessGuard SeesNothing() => new(new HashSet<Guid>());

    public Task<bool> CanReadAsync(Guid planId, CancellationToken cancellationToken = default) =>
        Task.FromResult(allowed is null || allowed.Contains(planId));
}
