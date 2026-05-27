using Microsoft.AspNetCore.Http;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Infrastructure.Identity;

public sealed class TenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    public Guid TenantId
    {
        get
        {
            var httpContext = httpContextAccessor.HttpContext;

            // No HTTP context (background/test scope) or unauthenticated request — return sentinel.
            if (httpContext?.User.Identity?.IsAuthenticated != true)
                return Guid.Empty;

            // Authenticated request must carry a valid tenant_id claim.
            var claim = httpContext.User.FindFirst("tenant_id");

            if (claim is null || !Guid.TryParse(claim.Value, out var tenantId))
                throw new UnauthorizedAccessException("Request is missing a valid tenant_id claim.");

            return tenantId;
        }
    }

    public bool IsResolved => TenantId != Guid.Empty;
}
