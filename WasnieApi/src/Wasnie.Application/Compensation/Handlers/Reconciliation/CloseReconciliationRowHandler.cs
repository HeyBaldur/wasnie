using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Reconciliation;
using Wasnie.Application.Compensation.Queries.Reconciliation;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Reconciliation;

namespace Wasnie.Application.Compensation.Handlers.Reconciliation;

/// <summary>
/// Records that a person reviewed one row of the queue and decided to leave it as it stands.
///
/// ★★ IT WRITES ONLY THE NEW ROWS. The credit, the transaction, the plan and the alerts behind the
/// anomaly are never loaded for modification, let alone modified: the queue derives from them, and a
/// closure is a fact placed BESIDE them. This is what "el crédito original permanece inmutable"
/// means in code — not a flag someone remembered not to set, but a handler that has no reference to
/// the entity to set one on.
/// </summary>
public sealed class CloseReconciliationRowHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuthorizationService authorizationService)
    : IRequestHandler<CloseReconciliationRowCommand, Result<CloseReconciliationRowResult>>
{
    public async Task<Result<CloseReconciliationRowResult>> Handle(
        CloseReconciliationRowCommand request, CancellationToken ct)
    {
        // ★ Reconciliation.Close, not Reports.ViewAll. Reading the queue and deciding what leaves it
        // are different rights — see Permission.ReconciliationClose.
        await authorizationService.RequireAsync(Permission.ReconciliationClose, ct);

        if (string.IsNullOrWhiteSpace(request.Note))
            return Result<CloseReconciliationRowResult>.Failure("A closure must state why the anomaly is being left as it stands.");

        var kind = (int)request.Kind;

        // ★★ THE FACTS COME FROM THE LIVE QUEUE, AND THAT IS A SECURITY PROPERTY, not a convenience.
        // The closure's FactOccurredAt decides for how long a row stays hidden; taking it from the
        // request would let a caller post a distant future date and silence anomalies nobody has
        // detected yet. Seeds() is the same expression the screen reads, so what gets closed is
        // exactly what the reviewer was looking at.
        //
        // ★ Seeds(), NOT Filtered(): an anomaly already closed must not be closed twice, and
        // Filtered has excluded it. What remains here is the still-open part of the row.
        var open = await ReconciliationQuery
            .ExcludeClosed(db, ReconciliationQuery.Seeds(db))
            .Where(s => s.Kind == kind && s.EntityId == request.EntityId)
            .Select(s => new { s.Reason, s.OccurredAt, s.PayeeId, s.FactKey })
            .Distinct()
            .ToListAsync(ct);

        // Nothing open under this key: either it was never in the queue, or somebody fixed it — or
        // closed it — between the screen loading and this call. Reported rather than silently
        // treated as a success, so the UI can refresh instead of showing a closure that closed
        // nothing (§B1).
        if (open.Count == 0)
            return Result<CloseReconciliationRowResult>.Failure("This row is no longer open in the reconciliation queue.");

        var now = clock.UtcNowOffset;
        var actor = currentUser.UserId ?? "system";
        var email = currentUser.Email ?? string.Empty;

        // ★ ONE CLOSURE PER REASON. A row failing for two things is two judgements; recording one
        // entry for the pair would make "which anomaly did they actually review?" unanswerable, and
        // would hide a reason that recurs on its own later.
        foreach (var fact in open)
        {
            db.ReconciliationClosures.Add(ReconciliationClosure.Create(
                id: guid.NewGuid(),
                tenantId: tenantContext.TenantId,
                entryKind: kind,
                entityId: request.EntityId,
                reason: fact.Reason,
                factOccurredAt: fact.OccurredAt,
                // ★ The identity when the fact has one. Without it a re-observed alert would expire
                // this closure on the next CRM sync — see ReconciliationQuery.ExcludeClosed.
                factKey: fact.FactKey,
                note: request.Note,
                payeeId: fact.PayeeId,
                closedAt: now,
                closedByUserId: actor,
                closedByEmail: email));
        }

        await db.SaveChangesAsync(ct);

        return Result<CloseReconciliationRowResult>.Success(new CloseReconciliationRowResult(
            EntityId: request.EntityId,
            Kind: request.Kind,
            ClosedReasons: open.Select(f => f.Reason).OrderBy(r => r).ToList()));
    }
}
