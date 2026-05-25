namespace Wasnie.Application.Common.Interfaces;

public interface ITenantContext
{
    Guid TenantId { get; }
    bool IsResolved { get; }
}
