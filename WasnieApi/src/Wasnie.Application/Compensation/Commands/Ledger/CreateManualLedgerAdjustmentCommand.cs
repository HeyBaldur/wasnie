using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Ledger;

/// <summary>
/// A human adjustment to a payee's balance. <paramref name="Amount"/> is a POSITIVE magnitude —
/// the sign comes from <paramref name="TransactionType"/>, exactly as in the domain factory, so the
/// API cannot express "a forgiveness of minus 200".
/// </summary>
public sealed record CreateManualLedgerAdjustmentCommand(
    Guid PayeeId,
    string TransactionType,
    decimal Amount,
    string Currency,
    string Justification) : IRequest<Result<PayeeLedgerEntryDto>>;
