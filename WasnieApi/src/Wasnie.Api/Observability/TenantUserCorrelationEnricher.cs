using System.Security.Claims;
using Serilog.Core;
using Serilog.Events;

namespace Wasnie.Api.Observability;

// Enriches every log event with CorrelationId, TenantId, and UserId from the current HTTP context.
// Resolved from DI via ReadFrom.Services() so it has access to IHttpContextAccessor.
public sealed class TenantUserCorrelationEnricher(IHttpContextAccessor httpContextAccessor) : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext == null) return;

        var correlationId = httpContext.Items["CorrelationId"] as string;
        if (!string.IsNullOrEmpty(correlationId))
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", correlationId));

        var tenantId = httpContext.User.FindFirst("tenant_id")?.Value;
        if (!string.IsNullOrEmpty(tenantId))
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TenantId", tenantId));

        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("UserId", userId));
    }
}
