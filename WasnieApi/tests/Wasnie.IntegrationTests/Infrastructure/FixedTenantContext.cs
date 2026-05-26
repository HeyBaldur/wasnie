using Wasnie.Application.Common.Interfaces;

namespace Wasnie.IntegrationTests.Infrastructure;

internal sealed class FixedTenantContext(Guid tenantId) : ITenantContext
{
    public Guid TenantId => tenantId;
    public bool IsResolved => true;
}
