using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Application.Compensation.Calculation;

public interface IQuotaAttainmentService
{
    /// <summary>
    /// Returns the attainment ratio for a given payee + plan as of a specific date.
    /// The method is scoped per request and caches results — calling it multiple times
    /// with the same triple within one request incurs only one DB hit.
    /// Returns <see cref="AttainmentPercentage.Zero"/> when no matching active/closed quota exists.
    /// </summary>
    Task<AttainmentPercentage> ComputeAsync(
        Guid payeeId,
        Guid planId,
        DateOnly asOfDate,
        CancellationToken ct = default);
}
