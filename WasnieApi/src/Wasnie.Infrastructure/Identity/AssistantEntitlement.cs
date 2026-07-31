using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Authorization;

namespace Wasnie.Infrastructure.Identity;

/// <summary>
/// The current answer to "may this user use the assistant?": tenant admins only.
///
/// ★ THE ROLE CHECK LIVES HERE AND NOWHERE ELSE. It is an implementation detail of the entitlement,
/// not the definition of it — see <see cref="IAssistantEntitlement"/> for why the distinction matters.
///
/// ★ HOW THIS BECOMES PER-SEAT BILLING, so the next person does not have to guess: the seat is a
/// per-USER flag hanging off the tenant's subscription. When that exists, this method becomes
/// "admin OR the user's seat is active", and later just "the user's seat is active" once admins get a
/// seat of their own at signup. Either way it is an edit INSIDE this class — the endpoints, the
/// handlers, the frontend gate and the tests all keep calling the same question and never learn that
/// the answer changed shape. That is the entire design.
/// </summary>
public sealed class AssistantEntitlement(IClaimsService claimsService) : IAssistantEntitlement
{
    private const string Feature = "Assistant.Use";

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        var role = claimsService.GetRole();

        // Today: the tenant admin, and only the tenant admin. Tomorrow: this line reads the seat.
        var enabled = string.Equals(role, nameof(Role.TenantAdmin), StringComparison.Ordinal);

        return Task.FromResult(enabled);
    }

    public async Task RequireAsync(CancellationToken cancellationToken = default)
    {
        if (await IsEnabledAsync(cancellationToken))
            return;

        // Deliberately NOT audited the way a denied permission is. A user without a seat hitting the
        // assistant is a billing state, not an attempt to exceed their authority — logging it as a
        // security denial would fill the audit trail with noise the day seats are sold.
        throw new ForbiddenException(Feature);
    }
}
