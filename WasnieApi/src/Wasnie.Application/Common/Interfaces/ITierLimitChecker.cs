namespace Wasnie.Application.Common.Interfaces;

public interface ITierLimitChecker
{
    Task EnsurePayeeLimitAsync(CancellationToken cancellationToken = default);
    Task EnsurePlanLimitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether importing <paramref name="incomingCount"/> payees would exceed the tenant's tier limit.
    /// Does NOT throw — returns structured result so callers can return domain-specific HTTP responses.
    /// Logs the denial to the audit trail if blocked.
    /// </summary>
    Task<PayeeImportLimitCheck> CheckPayeeImportLimitAsync(int incomingCount, CancellationToken cancellationToken = default);
}

/// <summary>Result of a bulk-import tier limit pre-flight check.</summary>
public sealed record PayeeImportLimitCheck(bool Blocked, int Current, int Limit, string Tier);
