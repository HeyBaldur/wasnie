using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Transactions;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Transactions;

/// <summary>
/// Voids many transactions in one operation (WI bulk-void). Money-critical + anti-double-pay (Rule 10):
/// - Only Pending transactions can be voided (enforced by <c>Cancel()</c>); Paid/Calculated/Cancelled are
///   rejected with a reason — never silently.
/// - A transaction with ANY ACTIVE credit is refused (mirrors the single-void shield), so voiding can never
///   strand consumed/paid credits.
/// Batch + no N+1: one query for the transactions, one grouped query for active credits; the voids and their
/// per-transaction audit entries commit atomically in a single SaveChanges.
/// </summary>
public sealed class BulkVoidTransactionsHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuthorizationService authorizationService)
    : IRequestHandler<BulkVoidTransactionsCommand, Result<BulkVoidResult>>
{
    public async Task<Result<BulkVoidResult>> Handle(
        BulkVoidTransactionsCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsVoid, cancellationToken);

        if (request.TransactionIds.Count == 0)
            return Result<BulkVoidResult>.Success(new BulkVoidResult(0, []));

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 3)
            return Result<BulkVoidResult>.Failure("A void reason of at least 3 characters is required.");

        var reason = request.Reason.Trim();
        var ids = request.TransactionIds.Distinct().ToList();

        var transactions = await db.CompensationTransactions
            .Where(t => ids.Contains(t.Id))
            .ToListAsync(cancellationToken);

        // Shield, batched: transactions that still have an active (non-superseded) credit cannot be voided.
        var withActiveCredits = (await db.Credits
            .Where(c => ids.Contains(c.TransactionId) && c.SupersededAt == null)
            .Select(c => c.TransactionId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

        var now = clock.UtcNowOffset;
        var actor = currentUser.UserId ?? "system";
        var actorEmail = currentUser.Email ?? actor;
        var voided = 0;
        var errors = new List<string>();

        var foundIds = transactions.Select(t => t.Id).ToHashSet();
        foreach (var missing in ids.Where(id => !foundIds.Contains(id)))
            errors.Add($"Transaction {missing}: not found.");

        foreach (var tx in transactions)
        {
            if (withActiveCredits.Contains(tx.Id))
            {
                errors.Add($"{tx.ReferenceNumber}: has active credits — reassign or supersede them before voiding.");
                continue;
            }

            var beforeStatus = tx.Status.ToString();
            try
            {
                tx.Cancel(reason, actor, now, guid.NewGuid());
            }
            catch (DomainException ex)
            {
                errors.Add($"{tx.ReferenceNumber}: {ex.Message}");
                continue;
            }

            voided++;
            db.AuditLogs.Add(AuditLog.Create(
                tenantId: tx.TenantId,
                timestampUtc: clock.UtcNow,
                actorUserId: actor,
                actorEmail: actorEmail,
                action: "transaction_voided",
                resourceType: "CompensationTransaction",
                resourceId: tx.Id.ToString(),
                resourceDisplayName: tx.ReferenceNumber,
                beforeJson: JsonSerializer.Serialize(new { status = beforeStatus }),
                afterJson: JsonSerializer.Serialize(new { status = "Cancelled", reason })));
        }

        // Voids + audit entries in ONE SaveChanges → a single DB transaction (atomic; audit never lost).
        await db.SaveChangesAsync(cancellationToken);

        return Result<BulkVoidResult>.Success(new BulkVoidResult(voided, errors));
    }
}
