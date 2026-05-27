using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Infrastructure.Services.Audit;

public sealed class AuditService(IAuditDispatcher dispatcher) : IAuditService
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
        dispatcher.DispatchAsync(entry, cancellationToken);
}
