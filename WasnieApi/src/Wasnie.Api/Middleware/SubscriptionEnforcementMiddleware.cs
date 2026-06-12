using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Subscription;

namespace Wasnie.Api.Middleware;

/// <summary>
/// Blocks all functional endpoints for tenants with a Canceled subscription.
/// Exempt: auth, health, and the subscription endpoints needed to display the
/// reactivation screen and initiate a new checkout.
/// </summary>
public sealed class SubscriptionEnforcementMiddleware(RequestDelegate next)
{
    private static readonly string[] ExemptPrefixes =
    [
        "/api/subscription/webhook",
        "/api/subscription/current",
        "/api/subscription/checkout",
        "/api/subscription/config",
        "/api/subscription/plans",
        "/api/subscription/usage",
        "/api/auth/",
        "/health",
    ];

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

        var db = context.RequestServices.GetRequiredService<IApplicationDbContext>();
        var status = await db.UserSubscriptions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantContext.TenantId)
            .Select(s => (SubscriptionStatus?)s.Status)
            .FirstOrDefaultAsync(context.RequestAborted);

        if (status == SubscriptionStatus.Canceled)
        {
            logger.LogWarning(
                "Blocked {Method} {Path} — tenant {TenantId} has a Canceled subscription",
                context.Request.Method,
                path,
                tenantContext.TenantId);

            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                """{"code":"subscription_canceled","message":"Subscription canceled. Reactivate your subscription to continue."}""",
                context.RequestAborted);
            return;
        }

        await next(context);
    }
}
