using Wasnie.Application.Common.DTOs;

namespace Wasnie.Application.Common.Interfaces;

public interface IAuditDispatcher
{
    Task DispatchAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
