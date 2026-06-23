using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Transactions;

/// <summary>Void (cancel) many transactions at once. Reason is shared across the batch (min 3 chars).</summary>
public sealed record BulkVoidTransactionsCommand(IReadOnlyList<Guid> TransactionIds, string Reason)
    : IRequest<Result<BulkVoidResult>>;

/// <summary>
/// Outcome of a bulk void. <see cref="VoidedCount"/> succeeded; <see cref="Errors"/> lists each transaction
/// that could NOT be voided with a human-readable reason (already paid/calculated, has active credits, not
/// found, etc.) — never failed silently.
/// </summary>
public sealed record BulkVoidResult(int VoidedCount, IReadOnlyList<string> Errors);
