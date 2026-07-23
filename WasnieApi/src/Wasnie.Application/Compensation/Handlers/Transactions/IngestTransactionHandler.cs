using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Constants;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Commands.Transactions;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Transactions;

public sealed class IngestTransactionHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuthorizationService authorizationService,
    IFieldRequirementService fieldRequirements,
    ICreditAllocationService creditAllocationService,
    Wasnie.Application.Compensation.Common.ITransactionCreateGuard createGuard)
    : IRequestHandler<IngestTransactionCommand, Result<TransactionDto>>
{
    public async Task<Result<TransactionDto>> Handle(IngestTransactionCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsCreate, cancellationToken);

        var payeeIdRequired = await fieldRequirements.IsRequiredAsync(
            TransactionFieldNames.Entity, TransactionFieldNames.PayeeId, cancellationToken);

        if (request.PayeeId is null)
        {
            if (payeeIdRequired)
                return Result<TransactionDto>.Failure("Payee is required for this tenant. Please assign a payee.");
        }
        else
        {
            var payee = await db.Payees
                .FirstOrDefaultAsync(p => p.Id == request.PayeeId.Value, cancellationToken);

            if (payee is null)
                return Result<TransactionDto>.Failure($"Payee '{request.PayeeId}' not found.");

            if (!payee.IsActive)
                return Result<TransactionDto>.Failure($"Payee '{request.PayeeId}' is inactive. Use import to assign historical transactions to inactive payees.");
        }

        // Centralized create rule (same rule for HubSpot/Excel/Manual): an ACTIVE row with this reference
        // is a real duplicate; a voided row with this reference does NOT block (Opción B) unless it carried
        // credits (anti-double-pay). Manual transactions have no ExternalId.
        var classification = await createGuard.ClassifyAsync(
            Wasnie.Domain.Compensation.Enums.TransactionSource.Manual,
            [request.ReferenceNumber], [null], cancellationToken);
        switch (classification.Decide(request.ReferenceNumber, null))
        {
            case Wasnie.Application.Compensation.Common.TransactionCreateDecision.SkipActiveDuplicate:
                return Result<TransactionDto>.Failure(
                    $"A transaction with reference '{request.ReferenceNumber}' already exists.");
            case Wasnie.Application.Compensation.Common.TransactionCreateDecision.BlockedVoidHadCredits:
                return Result<TransactionDto>.Failure(
                    $"Reference '{request.ReferenceNumber}' belongs to a voided transaction that was already processed and cannot be reused.");
        }

        Money amount;
        try
        {
            amount = Money.Of(request.Amount, request.Currency);
        }
        catch (DomainException ex)
        {
            return Result<TransactionDto>.Failure(ex.Message);
        }

        // ── Plan attribution (money-critical) ─────────────────────────────────────────────────
        // A payee on several applicable plans used to have the plan picked by an arbitrary tie-break
        // (shortest period, then smallest Id), which silently decided how much commission was paid.
        // The admin must now state the plan; the server re-validates rather than trusting the client.
        var attributionError = await ValidatePlanAttributionAsync(request, cancellationToken);
        if (attributionError is not null)
            return Result<TransactionDto>.Failure(attributionError);

        var txId = guid.NewGuid();
        var now = clock.UtcNowOffset;

        var tx = CompensationTransaction.Ingest(
            tenantId: tenantContext.TenantId,
            referenceNumber: request.ReferenceNumber,
            payeeId: request.PayeeId,
            amount: amount,
            transactionDate: request.TransactionDate,
            source: TransactionSource.Manual,
            ingestedBy: currentUser.UserId ?? "system",
            id: txId,
            now: now,
            eventId: guid.NewGuid(),
            quantity: request.Quantity,
            description: request.Description,
            selectedPlanAssignmentId: request.SelectedPlanAssignmentId);

        db.CompensationTransactions.Add(tx);
        await db.SaveChangesAsync(cancellationToken);

        if (request.ProcessImmediately)
        {
            // Allocate credits immediately after ingest (Decision #44: null payee = no credits).
            var credits = await creditAllocationService.AllocateAsync(tx, cancellationToken);
            foreach (var credit in credits)
                db.Credits.Add(credit);

            if (credits.Count > 0)
            {
                var total = credits.Skip(1).Aggregate(credits[0].CreditedAmount,
                    (acc, c) => acc.Add(c.CreditedAmount));
                tx.MarkCalculated(credits.Count, total, currentUser.UserId ?? "system",
                    clock.UtcNowOffset, guid.NewGuid());
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        request.AuditResourceId = tx.Id.ToString();

        return Result<TransactionDto>.Success(ToDto(tx));
    }

    /// <summary>
    /// Returns null when the attribution is acceptable, otherwise the message to fail with.
    ///
    /// The ambiguity threshold is the candidate COUNT from <see cref="PayeePlanCandidates"/> — the
    /// engine's own eligibility rule — so the form and this check can never disagree about when a
    /// choice is required. Rules: 2+ candidates → a choice is mandatory and must be one of them;
    /// 0 or 1 candidate → no choice needed, but anything supplied must still be a real candidate
    /// (a stale or hand-crafted id must never reach the engine).
    /// </summary>
    private async Task<string?> ValidatePlanAttributionAsync(
        IngestTransactionCommand request, CancellationToken ct)
    {
        if (request.PayeeId is null)
        {
            return request.SelectedPlanAssignmentId.HasValue
                ? "A plan cannot be selected for a transaction with no payee."
                : null;
        }

        var candidates = await PayeePlanCandidates.LoadAsync(
            db, tenantContext.TenantId, request.PayeeId.Value,
            request.TransactionDate, request.Currency, ct);

        if (candidates.Count >= 2 && request.SelectedPlanAssignmentId is null)
            return "This payee is on more than one applicable plan. Select which plan this transaction belongs to.";

        if (request.SelectedPlanAssignmentId is { } selectedId &&
            !candidates.Any(c => c.Id == selectedId))
            return "The selected plan is not applicable to this payee for this transaction's date and currency.";

        return null;
    }

    internal static TransactionDto ToDto(CompensationTransaction tx) =>
        new(tx.Id, tx.TenantId, tx.ReferenceNumber, tx.PayeeId,
            tx.Amount.Amount, tx.Amount.Currency, tx.Quantity, tx.TransactionDate,
            tx.Source.ToString(), tx.Status.ToString(), tx.ExternalId,
            tx.IngestedAt, tx.IngestedBy, tx.UpdatedAt,
            Description: tx.Description,
            SelectedPlanAssignmentId: tx.SelectedPlanAssignmentId,
            CancelledBy: tx.CancelledBy,
            CancelledAt: tx.CancelledAt,
            CancelledReason: tx.CancelledReason);
}
