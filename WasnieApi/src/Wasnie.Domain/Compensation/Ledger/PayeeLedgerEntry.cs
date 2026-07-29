using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Events;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Ledger;

/// <summary>
/// One APPEND-ONLY entry in a payee's balance ledger.
///
/// Design rules this type enforces, not documents:
///   • No mutation. There is no setter, no Update(), no Delete path. A wrong entry is corrected by
///     writing a NEW entry of the opposite sign — never by editing history.
///   • The sign is derived from <see cref="LedgerTransactionType"/>, so a debit stored as a positive
///     amount is unrepresentable. Callers pass a positive magnitude.
///   • <see cref="Origin"/> is set by the factory, never by the caller: <see cref="CreateSystemEntry"/>
///     stamps System, <see cref="CreateManualAdjustment"/> stamps Human.
///   • Justification is validated in the private constructor — an entry without a reason never exists.
///
/// SCOPE: the ledger is global per (TenantId, PayeeId, Currency) — NOT partitioned by plan. A debt
/// created under one plan is netted against commissions earned under any other plan of the same
/// payee in the same currency. The currency dimension is not a business partition, it is the
/// arithmetic invariant of <see cref="Money"/>: cross-currency operations throw, and Wasnie holds no
/// FX rates (Spec §5b.5 — "each plan in its own currency, no mixing").
/// </summary>
public sealed class PayeeLedgerEntry : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid PayeeId { get; private set; }

    /// <summary>WHO created it. Immutable — no method on this type ever writes it after construction.</summary>
    public LedgerEntryOrigin Origin { get; private set; }

    /// <summary>WHAT it means accounting-wise. Drives the sign of <see cref="Amount"/>.</summary>
    public LedgerTransactionType TransactionType { get; private set; }

    /// <summary>
    /// SIGNED amount. Negative = the payee owes it (debit); positive = in the payee's favour.
    /// The running balance is the plain sum of these, which is why the sign lives in the data and
    /// not in the reader.
    /// </summary>
    public Money Amount { get; private set; } = null!;

    /// <summary>Free text for the human auditor. Never parsed for metrics — that is what the enums are for.</summary>
    public string Justification { get; private set; } = string.Empty;

    // ── Traceability ──────────────────────────────────────────────────────────
    public LedgerSourceType? SourceType { get; private set; }
    public Guid? SourceTransactionId { get; private set; }
    public Guid? SourcePayRunId { get; private set; }
    public string? SourceExternalDealId { get; private set; }

    /// <summary>
    /// The plan whose policy produced this entry (churn clawback). Kept because MaturationDays is a PLAN
    /// setting: without it a multi-plan transaction leaves two debits nobody can attribute. Null for
    /// entries no plan produced (settlements, manual adjustments).
    /// </summary>
    public Guid? SourcePlanId { get; private set; }

    /// <summary>
    /// WHEN THE EVENT HAPPENED in the real world (the CRM's loss date), as opposed to
    /// <see cref="CreatedAt"/>, which is when Wasnie booked it.
    ///
    /// These are deliberately two different fields because a CRM lets someone record TODAY that a deal
    /// was lost in MARCH. The formula must use March (that is how long the deal really lived), but the
    /// money must be booked in the CURRENTLY OPEN period — booking it into March would alter a balance
    /// that was already reconciled and paid. So: EventDate feeds the arithmetic and the audit trail;
    /// CreatedAt is the accounting date. Never the other way round.
    /// </summary>
    public DateOnly? EventDate { get; private set; }

    // ── Clawback formula inputs, so the number is reproducible from the row ───
    // Only populated for a proportional (churn) clawback. A money figure nobody can recompute is a
    // figure nobody can defend in front of the person whose pay it reduced.
    public decimal? SourceCommissionAmount { get; private set; }
    public int? DaysActive { get; private set; }
    public int? MaturationDays { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    private PayeeLedgerEntry() { }

    /// <summary>
    /// Engine-written entry: clawback triggers and pay-run settlements. Stamps Origin = System.
    /// Not reachable from any user endpoint — the API surface only exposes
    /// <see cref="CreateManualAdjustment"/>.
    /// </summary>
    public static PayeeLedgerEntry CreateSystemEntry(
        Guid tenantId,
        Guid payeeId,
        LedgerTransactionType transactionType,
        Money magnitude,
        string justification,
        LedgerSourceType sourceType,
        string createdBy,
        Guid id,
        DateTimeOffset now,
        Guid eventId,
        Guid? sourceTransactionId = null,
        Guid? sourcePayRunId = null,
        string? sourceExternalDealId = null,
        decimal? sourceCommissionAmount = null,
        int? daysActive = null,
        int? maturationDays = null,
        Guid? sourcePlanId = null,
        DateOnly? eventDate = null)
    {
        var entry = new PayeeLedgerEntry(
            tenantId, payeeId, LedgerEntryOrigin.System, transactionType, magnitude,
            justification, createdBy, id, now)
        {
            SourceType = sourceType,
            SourceTransactionId = sourceTransactionId,
            SourcePayRunId = sourcePayRunId,
            SourceExternalDealId = sourceExternalDealId,
            SourceCommissionAmount = sourceCommissionAmount,
            DaysActive = daysActive,
            MaturationDays = maturationDays,
            SourcePlanId = sourcePlanId,
            EventDate = eventDate,
        };

        entry.RaiseDomainEvent(new PayeeLedgerEntryCreatedEvent(
            eventId, now, entry.Id, tenantId, payeeId,
            entry.Amount.Amount, entry.Amount.Currency, transactionType.ToString(), LedgerEntryOrigin.System.ToString()));

        return entry;
    }

    /// <summary>
    /// Human-written adjustment. Stamps Origin = Human.
    ///
    /// Every guard here is an invariant, not a validation convenience: the object does not come into
    /// existence without an owner, a reason, and a type a human is allowed to write.
    /// </summary>
    public static PayeeLedgerEntry CreateManualAdjustment(
        Guid tenantId,
        Guid payeeId,
        LedgerTransactionType transactionType,
        Money magnitude,
        string justification,
        string actor,
        Guid id,
        DateTimeOffset now,
        Guid eventId)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new DomainException("A manual ledger adjustment requires an actor — no owner, no entry.");

        if (!transactionType.IsManuallyCreatable())
            throw new DomainException(
                $"{transactionType} cannot be created by hand. Clawback debits come from a trigger and " +
                "settlements come from a payment; a human corrects them with a forgiveness or correction entry.");

        var entry = new PayeeLedgerEntry(
            tenantId, payeeId, LedgerEntryOrigin.Human, transactionType, magnitude,
            justification, actor, id, now);

        entry.RaiseDomainEvent(new PayeeLedgerEntryCreatedEvent(
            eventId, now, entry.Id, tenantId, payeeId,
            entry.Amount.Amount, entry.Amount.Currency, transactionType.ToString(), LedgerEntryOrigin.Human.ToString()));

        return entry;
    }

    private PayeeLedgerEntry(
        Guid tenantId,
        Guid payeeId,
        LedgerEntryOrigin origin,
        LedgerTransactionType transactionType,
        Money magnitude,
        string justification,
        string createdBy,
        Guid id,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("TenantId must not be empty.");
        if (payeeId == Guid.Empty)
            throw new DomainException("PayeeId must not be empty.");
        if (magnitude is null)
            throw new DomainException("A ledger entry requires an amount.");
        if (magnitude.Amount <= 0m)
            throw new DomainException(
                "A ledger entry is created from a POSITIVE magnitude; the sign comes from the transaction type. " +
                $"Received {magnitude}.");
        if (string.IsNullOrWhiteSpace(justification))
            throw new DomainException("A ledger entry requires a justification.");
        if (justification.Length > 1000)
            throw new DomainException("Justification must not exceed 1000 characters.");
        if (string.IsNullOrWhiteSpace(createdBy))
            throw new DomainException("CreatedBy is required.");

        Id = id;
        TenantId = tenantId;
        PayeeId = payeeId;
        Origin = origin;
        TransactionType = transactionType;
        Amount = transactionType.IsDebit() ? magnitude.Negate() : magnitude;
        Justification = justification.Trim();
        CreatedBy = createdBy;
        CreatedAt = now;
    }
}
