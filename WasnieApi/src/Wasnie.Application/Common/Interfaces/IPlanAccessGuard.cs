namespace Wasnie.Application.Common.Interfaces;

/// <summary>
/// Resource-level authorisation for plans: not "may this ROLE read plans" (that is
/// <see cref="IAuthorizationService"/>) but "may this USER read THIS plan".
///
/// ★★ IT EXISTS FOR THE SAME REASON <see cref="IPayeeAccessGuard"/> DOES, one level across. KAN-93
/// gives a Rep <c>Plans.ReadOwn</c> so they can check the arithmetic behind their own commission —
/// the rate table, the tiers, the cap, the floor. A permission alone cannot express "own": it
/// authorises a verb, and the question here is about an object. Without this class, granting the
/// permission would open every plan in the tenant to anybody holding it, which is the catalogue leak
/// the permission was deliberately narrowed to avoid.
///
/// ★ UNRESTRICTED IS A REAL ANSWER, NOT A BYPASS. Whoever holds <c>Plans.Read</c> — administrators,
/// compensation managers — reads the whole catalogue because running compensation IS reading the
/// catalogue. The guard says so explicitly rather than being skipped at the call site, so there is one
/// place that decides and no handler has to remember to ask twice.
/// </summary>
public interface IPlanAccessGuard
{
    /// <summary>Whether the asking user may read this plan and its rules.</summary>
    Task<bool> CanReadAsync(Guid planId, CancellationToken cancellationToken = default);
}
