using Wasnie.Domain.Common;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Reconciliation;

/// <summary>
/// A human's decision that one anomaly in the Reconciliation Centre has been reviewed and is to be
/// left as it stands: "seen, understood, nothing to repair."
///
/// ★★ AN ENTRY, NOT A FLAG, AND THAT IS THE WHOLE DESIGN. The queue derives from the data — a row
/// exists because a credit was refused or an alert is unresolved, never because a column says so
/// (KAN-28). A "closed" boolean on the credit would be the one mutable thing in a derived screen,
/// and the first thing to go stale. This is an append-only fact ABOUT the anomaly, sitting beside
/// it: the credit, the transaction and the plan are never touched.
///
/// ★★ ITS OWN TABLE, NOT AuditLogs, AND THAT IS NOT DUPLICATION. This record is LOAD-BEARING — the
/// screen hides rows according to it — and until KAN-34 the audit log recorded actions that never
/// happened. Evidence that decides what a CFO can see may not come from a table that has been known
/// to lie; AuditLogs still receives an informational entry, but the exclusion reads from here.
///
/// ★ NOTHING HERE IS EVER UPDATED OR DELETED. There is no Reopen, no Undo and no soft-delete flag:
/// a closure records that a person decided something at a moment, and that stays true afterwards
/// (§B6). Reopening is expressed by the anomaly recurring as a NEWER fact — see
/// <see cref="FactOccurredAt"/> — not by editing this row.
/// </summary>
public sealed class ReconciliationClosure : Entity
{
    public Guid TenantId { get; private set; }

    /// <summary>
    /// Which kind of thing was closed, as the queue's own <c>ReconciliationEntryKind</c> ordinal.
    ///
    /// ★ IT IS PART OF THE KEY BECAUSE AN ID ALONE IS NOT UNIQUE ACROSS KINDS. A credit and a
    /// transaction are different rows that could in principle carry the same Guid; the queue keys
    /// its rows on the pair, and so does this.
    /// </summary>
    public int EntryKind { get; private set; }

    /// <summary>The credit, transaction or plan the closed row was about. Never modified by this.</summary>
    public Guid EntityId { get; private set; }

    /// <summary>
    /// The single reason being closed, as a <c>ReconciliationReason</c> code.
    ///
    /// ★★ ONE CLOSURE PER REASON, NOT ONE PER ROW. A row that fails for two things is two facts a
    /// person may judge separately, and closing the row writes one of these per reason it carried at
    /// the time. Keying on the entity alone would mean that closing a lost deal today silently
    /// swallowed a CRM drift detected on it tomorrow — money hidden without anyone deciding to hide
    /// it (§B1).
    ///
    /// ★ A CODE, NEVER PROSE (§C1). The screen owns the wording and the language.
    /// </summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>
    /// The timestamp of the FACT that was closed — the row's own <c>OccurredAt</c>: an alert's
    /// DetectedAt, a credit's AllocatedAt, a transaction's IngestedAt, a plan's UpdatedAt.
    ///
    /// ★★ THIS IS WHAT MAKES THE CLOSURE IMMUTABLE AND STILL SAFE. The closure suppresses the row
    /// only while the anomaly is no newer than the one that was reviewed. A fresh detection carries a
    /// later stamp, falls outside this closure, and comes back as a NEW row — which is exactly the
    /// product decision ("el cierre es inmutable; un cambio posterior es un hecho nuevo → fila
    /// nueva") rather than a revive that would have to mutate this record.
    ///
    /// Without it the alternative is a closure that hides an anomaly for ever, including anomalies
    /// that had not happened yet when the person decided.
    /// </summary>
    public DateTimeOffset FactOccurredAt { get; private set; }

    /// <summary>
    /// Why the person left it as it stands. MANDATORY — see <see cref="Create"/>.
    ///
    /// ★ PROSE ON PURPOSE, AND THE ONE PLACE IT BELONGS. §C1 bans prose the SYSTEM emits, because a
    /// machine-written sentence has to be redeployed to be translated. This sentence is written BY a
    /// human FOR a human auditor, in whatever language they work in. Coding it would be asking the
    /// reviewer to pick from a list of excuses nobody can foresee.
    /// </summary>
    public string Note { get; private set; } = string.Empty;

    public Guid? PayeeId { get; private set; }

    public DateTimeOffset ClosedAt { get; private set; }
    public string ClosedByUserId { get; private set; } = string.Empty;
    public string ClosedByEmail { get; private set; } = string.Empty;

    private ReconciliationClosure() { }

    /// <summary>
    /// ★ THE NOTE IS ENFORCED HERE, NOT ONLY IN THE MODAL (§D2). The UI blocking an empty box is a
    /// courtesy; this is the invariant. A closure with no stated reason is exactly the row an auditor
    /// would ask about, and the API is reachable without the form.
    /// </summary>
    public static ReconciliationClosure Create(
        Guid id,
        Guid tenantId,
        int entryKind,
        Guid entityId,
        string reason,
        DateTimeOffset factOccurredAt,
        string note,
        Guid? payeeId,
        DateTimeOffset closedAt,
        string closedByUserId,
        string closedByEmail)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("TenantId must not be empty.");
        if (entityId == Guid.Empty)
            throw new DomainException("EntityId must not be empty.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A closure must name the reason it closes.");
        if (string.IsNullOrWhiteSpace(note))
            throw new DomainException("A closure must state why the anomaly is being left as it stands.");
        if (string.IsNullOrWhiteSpace(closedByUserId))
            throw new DomainException("ClosedByUserId is required.");

        return new ReconciliationClosure
        {
            Id = id,
            TenantId = tenantId,
            EntryKind = entryKind,
            EntityId = entityId,
            Reason = reason,
            FactOccurredAt = factOccurredAt,
            Note = note.Trim(),
            PayeeId = payeeId,
            ClosedAt = closedAt,
            ClosedByUserId = closedByUserId,
            ClosedByEmail = closedByEmail,
        };
    }
}
