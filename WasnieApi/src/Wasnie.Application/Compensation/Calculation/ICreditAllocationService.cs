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
    /// <param name="alreadyCredited">
    /// (TransactionId, PlanId, RuleId) triples that already hold a live credit; those rules are
    /// skipped. Load it once per batch with <see cref="LoadLiveCreditKeysAsync"/> — passing null
    /// means "nothing is credited yet", which is only correct for freshly created transactions.
    /// </param>
    Task<IReadOnlyList<Credit>> AllocateAsync(
        CompensationTransaction transaction,
        IReadOnlyDictionary<Guid, IReadOnlyList<PlanAssignment>> assignmentsByPayee,
        IReadOnlyDictionary<Guid, CompensationPlan> plansById,
        IReadOnlySet<(Guid TransactionId, Guid PlanId, Guid RuleId)>? alreadyCredited = null,
        CancellationToken ct = default);

    /// <summary>
    /// The (TransactionId, PlanId, RuleId) triples holding a live (non-superseded) credit, for the
    /// given transactions. One bounded query — the batch caller loads the whole chunk up front so the
    /// per-transaction path stays free of DB round-trips.
    /// </summary>
    Task<IReadOnlySet<(Guid TransactionId, Guid PlanId, Guid RuleId)>> LoadLiveCreditKeysAsync(
        IReadOnlyCollection<Guid> transactionIds,
        CancellationToken ct = default);
}
