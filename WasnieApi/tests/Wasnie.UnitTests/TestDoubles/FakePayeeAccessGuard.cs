using Wasnie.Application.Authorization;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.UnitTests.TestDoubles;

/// <summary>
/// A payee-access guard with a fixed answer, for tests about something OTHER than authorisation
/// (paging defaults, period filters, DTO shape).
///
/// ★ THE DEFAULT IS DELIBERATELY <see cref="SeesEverything"/> — the finance/supervisor answer — so those
/// tests keep asserting what they were written to assert. That is only safe because the guard's own
/// behaviour is pinned elsewhere, by tests that use the REAL implementation: PayeeAccessGuardTests
/// (unit) and PayeeResourceAuthorizationTests / PayeeScopedEndpointAuthorizationTests (HTTP + SQL). If
/// this double ever becomes the only thing standing behind an endpoint's guard, that endpoint is
/// untested — reach for the real one instead of widening this.
/// </summary>
internal sealed class FakePayeeAccessGuard(PayeeVisibility visibility) : IPayeeAccessGuard
{
    public static FakePayeeAccessGuard SeesEverything() => new(PayeeVisibility.Everything);

    public static FakePayeeAccessGuard Sees(params Guid[] payeeIds) => new(PayeeVisibility.Of(payeeIds));

    public static FakePayeeAccessGuard SeesNothing() => new(PayeeVisibility.None);

    public Task<PayeeVisibility> GetVisibilityAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(visibility);

    public Task<bool> CanReadAsync(Guid payeeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(visibility.Allows(payeeId));
}
