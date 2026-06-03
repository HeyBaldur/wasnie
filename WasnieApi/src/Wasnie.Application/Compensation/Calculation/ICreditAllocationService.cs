using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Transactions;
using CompensationPlan = Wasnie.Domain.Compensation.Plans.Plan;

namespace Wasnie.Application.Compensation.Calculation;

public interface ICreditAllocationService
{
    /// <summary>Single-transaction path — queries DB per invocation.</summary>
    Task<IReadOnlyList<Credit>> AllocateAsync(
        CompensationTransaction transaction,
        CancellationToken ct = default);

    /// <summary>
    /// Batch-optimised path — caller supplies pre-loaded lookups so no DB queries are needed
    /// inside this method. Use this when processing many transactions in a loop.
    /// </summary>
    Task<IReadOnlyList<Credit>> AllocateAsync(
        CompensationTransaction transaction,
        IReadOnlyDictionary<Guid, IReadOnlyList<PlanAssignment>> assignmentsByPayee,
        IReadOnlyDictionary<Guid, CompensationPlan> plansById,
        CancellationToken ct = default);
}
