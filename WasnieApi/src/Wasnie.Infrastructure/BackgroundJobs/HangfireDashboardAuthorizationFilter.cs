using Hangfire.Dashboard;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Wasnie.Infrastructure.BackgroundJobs;

/// <summary>
/// Guards the Hangfire dashboard. In Development: requires authentication.
/// In Production: blocked until a global SystemAdmin role/claim is implemented
/// (the dashboard exposes cross-tenant job data and must never be public).
/// See docs/14-forbidden-patterns.md — "Dashboard must be admin-only".
/// </summary>
public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var env = httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();

        if (!env.IsDevelopment())
            return false;

        return httpContext.User.Identity?.IsAuthenticated == true;
    }
}
