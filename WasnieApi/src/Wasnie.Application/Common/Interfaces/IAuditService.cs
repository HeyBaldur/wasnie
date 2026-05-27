using Wasnie.Application.Common.DTOs;

namespace Wasnie.Application.Common.Interfaces;

public interface IAuditService
{
    Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
