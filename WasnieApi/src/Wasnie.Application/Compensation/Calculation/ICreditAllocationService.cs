using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Transactions;

namespace Wasnie.Application.Compensation.Calculation;

public interface ICreditAllocationService
{
    Task<IReadOnlyList<Credit>> AllocateAsync(
        CompensationTransaction transaction,
        CancellationToken ct = default);
}
