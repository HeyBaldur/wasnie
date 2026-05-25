using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Infrastructure.Identity;

public sealed class TenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    public Guid TenantId
    {
        get
        {
            var claim = httpContextAccessor.HttpContext?.User
                .FindFirst("tenant_id");

            if (claim is null || !Guid.TryParse(claim.Value, out var tenantId))
            {
                return Guid.Empty;
            }

            return tenantId;
        }
    }

    public bool IsResolved => TenantId != Guid.Empty;
}
