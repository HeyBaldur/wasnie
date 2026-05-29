using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Infrastructure.Identity;

/// <summary>
/// ITenantContext for background job scopes. MUST call SetTenant() before any code
/// that reads TenantId — deliberately throws if read before set (never silently returns Guid.Empty).
/// </summary>
public sealed class BackgroundJobTenantContext : ITenantContext
{
    private Guid? _tenantId;

    public void SetTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId must not be Guid.Empty.", nameof(tenantId));
        _tenantId = tenantId;
    }

    public Guid TenantId => _tenantId
        ?? throw new InvalidOperationException(
            "BackgroundJobTenantContext.TenantId was read before SetTenant() was called. " +
            "Call SetTenant(tenantId) at the start of every background job before any DB access.");

    public bool IsResolved => _tenantId.HasValue;
}
