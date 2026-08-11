using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.UnitTests.TestDoubles;

/// <summary>
/// The tenant's billing state, stated outright instead of stubbed per-call.
///
/// Defaults to a PAID plan so the hundreds of existing tests that care about a handler's own logic
/// keep exercising it: a gate that refused by default would turn every one of them into a test of the
/// gate. Pass <c>false</c> when the refusal IS the subject.
/// </summary>
public sealed class FakePaidPlanGate(bool onPaidPlan = true) : IPaidPlanGate
{
    public Task<bool> IsOnPaidPlanAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(onPaidPlan);

    public Task RequirePaidPlanAsync(string feature, CancellationToken cancellationToken = default)
        => onPaidPlan
            ? Task.CompletedTask
            : throw new PaidPlanRequiredException(feature, "Free", "Starter");
}
