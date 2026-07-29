using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Ledger;

/// <summary>
/// The churn trigger: a CRM deal whose commission was ALREADY PAID is now lost, so the unearned part of
/// that commission becomes a debt in the payee's ledger.
///
/// Deliberately a SEPARATE command from <c>RevertCommissionForLostDealCommand</c>, not a relaxation of it.
/// The revert supersedes credits and cancels the transaction — legitimate while the money is still
/// Calculated, and a contradiction once it has been paid (a consumed credit backs a payment that left the
/// company). This command touches NEITHER the credits nor the transaction: the payment history stays
/// exactly as it happened and the correction lives in the ledger, which is append-only. The revert must
/// keep refusing Paid; that refusal is a tested invariant, not an oversight this command works around.
///
/// System-triggered: there is no actor in the request because no human chooses the number. The engine
/// stamps Origin = System.
/// </summary>
/// <param name="TenantId">Explicit — the trigger runs from a background sync where there is no request scope.</param>
/// <param name="TransactionId">The Paid transaction whose deal died.</param>
/// <param name="EventDate">
/// The date the deal was LOST according to the CRM. Used for the arithmetic (how long the deal really
/// lived) and stored as evidence — never as the accounting date. See <c>PayeeLedgerEntry.EventDate</c>.
/// </param>
/// <param name="ExternalDealId">CRM deal id, kept on the entry so the debt traces back to its cause.</param>
public sealed record RegisterDealChurnClawbackCommand(
    Guid TenantId,
    Guid TransactionId,
    DateOnly EventDate,
    string? ExternalDealId = null) : IRequest<Result<DealChurnClawbackDto>>;
