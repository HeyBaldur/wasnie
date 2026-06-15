using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Api.Middleware;

/// <summary>
/// Enforces the activation funnel in order:
///   1. Email must be confirmed.
///   2. Qualification form must be complete.
/// Only after both steps is the user admitted to the rest of the app.
/// Runs for authenticated requests only; anonymous routes pass through.
/// </summary>
public sealed class ActivationEnforcementMiddleware(RequestDelegate next)
{
    // These paths are always allowed, regardless of activation state.
    private static readonly string[] AlwaysAllowed =
    [
        "/api/auth/",
        "/api/onboarding/qualify",
        "/health",
        "/swagger",
        "/jobs",
    ];

    // After email confirmation, these paths are also unlocked (but app is still gated).
    private static readonly string[] AllowedAfterConfirmation =
    [
        "/api/onboarding/",
    ];

    public async Task InvokeAsync(
        HttpContext context,
        ILogger<ActivationEnforcementMiddleware> logger)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;

        if (IsAlwaysAllowed(path))
        {
            await next(context);
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            await next(context);
            return;
        }

        // --- Gate 1: email confirmed ---
        var userManager = context.RequestServices.GetRequiredService<UserManager<IdentityUser>>();
        var user = await userManager.FindByIdAsync(userId);
        if (user is not null && !user.EmailConfirmed)
        {
            logger.LogInformation("Blocked {Path} — user {UserId} has not confirmed email", path, userId);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                """{"code":"email_not_confirmed","message":"Please confirm your email address to continue."}""",
                context.RequestAborted);
            return;
        }

        // After email confirmation, qualification-related paths are allowed.
        if (IsAllowedAfterConfirmation(path))
        {
            await next(context);
            return;
        }

        // --- Gate 2: qualification complete ---
        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();
        if (!tenantContext.IsResolved)
        {
            await next(context);
            return;
        }

        var db = context.RequestServices.GetRequiredService<IApplicationDbContext>();
        var isQualified = await db.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => (bool?)t.IsQualified)
            .FirstOrDefaultAsync(context.RequestAborted);

        if (isQualified == false)
        {
            logger.LogInformation(
                "Blocked {Path} — tenant {TenantId} has not completed qualification",
                path, tenantContext.TenantId);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                """{"code":"qualification_required","message":"Please complete the qualification form to continue."}""",
                context.RequestAborted);
            return;
        }

        await next(context);
    }

    private static bool IsAlwaysAllowed(string path)
    {
        foreach (var prefix in AlwaysAllowed)
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool IsAllowedAfterConfirmation(string path)
    {
        foreach (var prefix in AllowedAfterConfirmation)
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
