using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Ledger;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Common.DTOs;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Ledger;

/// <summary>
/// The ONLY write path a human has into the ledger. It calls the sealed domain factory
/// <see cref="PayeeLedgerEntry.CreateManualAdjustment"/> — there is deliberately no second way in,
/// so the invariants (positive magnitude, sign derived from the type, mandatory actor and
/// justification, engine-only types refused) hold for every entry that exists.
///
/// The actor comes from the authenticated user, never from the request body: an adjustment whose
/// owner the caller can choose is not an audit trail.
/// </summary>
public sealed class CreateManualLedgerAdjustmentHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ICurrentUserService currentUser,
    ITenantContext tenantContext,
    IClock clock,
    IGuidGenerator guid,
    IAuditService audit)
    : IRequestHandler<CreateManualLedgerAdjustmentCommand, Result<PayeeLedgerEntryDto>>
{
    public async Task<Result<PayeeLedgerEntryDto>> Handle(
        CreateManualLedgerAdjustmentCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.LedgerAdjust, cancellationToken);

        if (!Enum.TryParse<LedgerTransactionType>(request.TransactionType, ignoreCase: true, out var type))
            return Result<PayeeLedgerEntryDto>.Failure(
                $"Unknown adjustment type '{request.TransactionType}'.");

        var tenantId = tenantContext.TenantId;
        var actor = currentUser.Email ?? currentUser.UserId
            ?? throw new DomainException("A manual ledger adjustment requires an authenticated user.");
        var now = clock.UtcNowOffset;

        var payee = await db.Payees
            .Where(p => p.Id == request.PayeeId)
            .Select(p => new { p.Id, p.FullName })
            .FirstOrDefaultAsync(cancellationToken);

        if (payee is null)
            return Result<PayeeLedgerEntryDto>.Failure("Payee not found.");

        try
        {
            var magnitude = Money.Of(request.Amount, request.Currency);

            var entry = PayeeLedgerEntry.CreateManualAdjustment(
                tenantId, request.PayeeId, type, magnitude, request.Justification,
                actor, guid.NewGuid(), now, guid.NewGuid());

            // The balance row is opened on first touch. The unique index on
            // (tenant, payee, currency) is what stops two concurrent first-touches from creating
            // two balances that would each net against half the debt.
            var balance = await db.PayeeBalances
                .FirstOrDefaultAsync(
                    b => b.PayeeId == request.PayeeId && b.Currency == magnitude.Currency,
                    cancellationToken);

            if (balance is null)
            {
                balance = PayeeBalance.Open(tenantId, request.PayeeId, magnitude.Currency, guid.NewGuid(), now);
                db.PayeeBalances.Add(balance);
            }

            balance.Apply(entry, now);
            db.PayeeLedgerEntries.Add(entry);

            await db.SaveChangesAsync(cancellationToken);

            await audit.LogAsync(new AuditEntry(
                TenantId: tenantId,
                Action: AuditActions.LedgerAdjustmentCreated,
                ResourceType: ResourceTypes.Payee,
                ResourceId: request.PayeeId.ToString(),
                ActorUserId: currentUser.UserId ?? actor,
                ActorEmail: actor,
                DisplayName: $"{type} {entry.Amount} — {payee.FullName}",
                Metadata: new Dictionary<string, string>
                {
                    ["transactionType"] = type.ToString(),
                    ["signedAmount"] = entry.Amount.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["currency"] = entry.Amount.Currency,
                    ["balanceAfter"] = balance.Balance.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["justification"] = entry.Justification,
                }), cancellationToken);

            return Result<PayeeLedgerEntryDto>.Success(new PayeeLedgerEntryDto(
                entry.Id, entry.CreatedAt, entry.Origin.ToString(), entry.TransactionType.ToString(),
                entry.Amount.Amount, entry.Amount.Currency, entry.Justification, entry.CreatedBy,
                entry.SourceExternalDealId, entry.SourceTransactionId,
                entry.DaysActive, entry.MaturationDays, entry.SourceCommissionAmount,
                entry.EventDate, entry.SourcePlanId));
        }
        catch (DomainException ex)
        {
            // The domain owns the truth about what a valid adjustment is; the API just relays it.
            return Result<PayeeLedgerEntryDto>.Failure(ex.Message);
        }
    }
}
