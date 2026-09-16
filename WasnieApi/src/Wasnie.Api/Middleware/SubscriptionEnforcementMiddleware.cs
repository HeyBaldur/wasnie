using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Subscription;

namespace Wasnie.Api.Middleware;

/// <summary>
/// The paywall (KAN-77). Blocks every functional endpoint for a tenant whose account is
/// <see cref="AccountAccessState.Locked"/>: the trial ended without a subscription, or the subscription is no
/// longer active. Data is untouched — the moment they pay, the next request goes through.
///
/// Exempt: auth, health, and what a locked user needs to see the paywall and pay (plans, checkout, account
/// state, billing portal).
///
/// ★ The state comes from <see cref="IAccountAccessReader"/>, the same rule the metered-feature gate and the
/// account endpoint use, so the paywall cannot disagree with the banner about who is in.
/// </summary>
public sealed class SubscriptionEnforcementMiddleware(RequestDelegate next)
{
    private static readonly string[] ExemptPrefixes =
    [
        "/api/subscription/webhook",
        "/api/subscription/current",
        "/api/subscription/access",
        "/api/subscription/checkout",
        "/api/subscription/config",
        "/api/subscription/plans",
        "/api/subscription/usage",
        "/api/subscription/billing-portal",
        "/api/auth/",
        "/health",
    ];

    /// <summary>
    /// The 402 body's code. One code for every lock, with the reason beside it: the client shows ONE paywall,
    /// and a code per reason would make every new reason a client change.
    /// </summary>
    public const string LockedCode = "account_locked";

    public async Task InvokeAsync(
        HttpContext context,
        ILogger<SubscriptionEnforcementMiddleware> logger)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        foreach (var prefix in ExemptPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }
        }

        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();
        if (!tenantContext.IsResolved)
        {
            await next(context);
            return;
        }

        var reader = context.RequestServices.GetRequiredService<IAccountAccessReader>();
        var access = await reader.GetAsync(tenantContext.TenantId, context.RequestAborted);

        if (access is { HasAccess: false })
        {
            logger.LogWarning(
                "Blocked {Method} {Path} — tenant {TenantId} is locked ({Reason})",
                context.Request.Method,
                path,
                tenantContext.TenantId,
                access.LockReason);

            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            await context.Response.WriteAsJsonAsync(
                new { code = LockedCode, reason = access.LockReason?.ToString() },
                context.RequestAborted);
            return;
        }

        await next(context);
    }
}
