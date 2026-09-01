using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Events;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Credits;

public sealed class Credit : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid PayeeId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid RuleId { get; private set; }
    public RuleSnapshot RuleSnapshot { get; private set; } = null!;
    public Money OriginalAmount { get; private set; } = null!;
    public Money CreditedAmount { get; private set; } = null!;
    public Percentage SplitPercentage { get; private set; } = null!;
    public CreditRole Role { get; private set; }
    public DateTimeOffset AllocatedAt { get; private set; }
    public string AllocatedBy { get; private set; } = string.Empty;
    public DateTimeOffset? SupersededAt { get; private set; }
    public string? SupersededBy { get; private set; }
    // Anti-double-pay: set when a Paid payout consumes this credit; cleared on payout revert.
    public DateTimeOffset? ConsumedAt { get; private set; }
    public Guid? ConsumedByPayoutId { get; private set; }

    // ── The third ending: closed without ever being paid ──────────────────────────────────────────
    // ★★ A TERMINAL STATE WITH NO PAYOUT BEHIND IT, and it had to be new. Before this, a credit could
    // only leave circulation two ways and neither fitted: Consume demands a non-null payoutId — and
    // Unconsume dereferences it — so an invented GUID would leave the anti-double-pay trail pointing at
    // a payout that never existed; Supersede means "a reallocation replaced this one" and raises an
    // event the attainment queries read as exactly that. A departed payee's unpaid commission was
    // replaced by nothing and paid by nothing (docs/DIAG_ORPHAN_ACCOUNT_CLOSURE.md §3.1).
    //
    // ★ AND IT IS ONE-WAY. There is deliberately no Unclose: reversing a write-off is not an undo, it is
    // a new business decision with its own authority, and the ledger it travels with is append-only.
    public DateTimeOffset? ClosedAt { get; private set; }
    public string? ClosedBy { get; private set; }
    public CreditClosureReason? ClosureReason { get; private set; }

    /// <summary>
    /// Why this specific credit was closed, in the words of the person who closed it. Kept ON THE
    /// CREDIT and not only on the ledger entry: the ledger records a movement of a balance, and a
    /// balance is not a credit — six months later "what happened to this €3,869.34" has to be
    /// answerable from the row itself.
    /// </summary>
    public string? ClosureNote { get; private set; }

    /// <summary>Still owed and still payable: not replaced, not paid, not closed.</summary>
    public bool IsOutstanding => SupersededAt is null && ConsumedAt is null && ClosedAt is null;
    // Optimistic concurrency token — EF uses this to detect concurrent consumption of the same credit.
    /// <summary>
    /// How this credit was computed, as the document the engine emitted at the moment it ran. Null on
    /// every credit allocated before this column existed, and on any credit not produced by the
    /// engine.
    ///
    /// ★★ WHY IT IS STORED AND NOT RECOMPUTED. The inputs do not survive: quota attainment is
    /// as-of-a-date and keeps moving, so March's number is not what March's number was by the time
    /// somebody asks in November. Re-running the engine later answers a different question and
    /// answers it confidently. This is the only moment the reasoning exists.
    ///
    /// ★ OPAQUE TO THE DOMAIN, ON PURPOSE. Nothing here branches on it, nothing may derive money from
    /// it, and it is never rewritten — it is evidence, not state. It is held as the serialised
    /// document rather than a parsed object because the engine's trace type lives in the application
    /// layer, and the alternative was a second copy of its shape down here: two declarations of the
    /// same thing that agree until the day they do not, which is the failure this codebase has been
    /// bitten by before. The format is owned by <c>CalculationTraceSerializer</c>.
    ///
    /// ★ NEVER GOES BACK TO NULL. Same append-only reasoning as every other record of a fact here.
    /// </summary>
    public string? CalculationTrace { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    private Credit() { }

    public static Credit Allocate(
        Guid tenantId,
        Guid transactionId,
        Guid payeeId,
        Guid planId,
        Guid ruleId,
        RuleSnapshot ruleSnapshot,
        Money originalAmount,
        Money creditedAmount,
        Percentage splitPercentage,
        CreditRole role,
        string allocatedBy,
        Guid id,
        DateTimeOffset now,
        Guid eventId,
        // Defaulted deliberately, and it is not the "plausible lie" a defaulted DTO field would be:
        // null here means "no engine run produced this credit", which is exactly true of every
        // hand-built credit and of all 1,296 rows that predate the column. The production path passes
        // it explicitly; a caller that forgets it records an honest absence, not a wrong number.
        string? calculationTrace = null)
    {
        var credit = new Credit
        {
            Id = id,
            TenantId = tenantId,
            TransactionId = transactionId,
            PayeeId = payeeId,
            PlanId = planId,
            RuleId = ruleId,
            RuleSnapshot = ruleSnapshot,
            OriginalAmount = originalAmount,
            CreditedAmount = creditedAmount,
            SplitPercentage = splitPercentage,
            Role = role,
            AllocatedAt = now,
            AllocatedBy = allocatedBy,
            CalculationTrace = calculationTrace
        };

        credit.RaiseDomainEvent(new CreditAllocatedEvent(
            eventId, now, credit.Id, transactionId, payeeId, tenantId));

        return credit;
    }

    // Decision #46 Case A: mark this Credit superseded when the owning transaction is reassigned.
    // All non-superseded Credits for a Calculated transaction must be superseded before the transaction
    // is reassigned, so attainment queries never aggregate stale Credits.
    public void Supersede(string reason, DateTimeOffset now, Guid eventId)
    {
        if (SupersededAt is not null)
            throw new DomainException("Credit is already superseded.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Supersede reason is required.");
        if (reason.Length > 500)
            throw new DomainException("Supersede reason must not exceed 500 characters.");

        SupersededAt = now;
        SupersededBy = reason;

        RaiseDomainEvent(new CreditSupersededEvent(eventId, now, Id, TransactionId, PayeeId, TenantId, reason));
    }

    // Anti-double-pay Phase 3: mark this credit consumed when its payout is Paid.
    // ConsumedAt != null causes the calculation engine to exclude this credit from any future payout.
    public void Consume(Guid payoutId, DateTimeOffset now, Guid eventId)
    {
        if (ConsumedAt is not null)
            throw new DomainException($"Credit {Id} is already consumed by payout {ConsumedByPayoutId}.");

        ConsumedAt = now;
        ConsumedByPayoutId = payoutId;

        RaiseDomainEvent(new CreditConsumedEvent(eventId, now, Id, TransactionId, PayeeId, TenantId, payoutId));
    }

    /// <summary>
    /// Closes this credit for good, because the account of the payee who earned it was closed.
    ///
    /// ★ FAIL-CLOSED ON EVERY OTHER STATE. A consumed credit was already paid and closing it would
    /// double-count the closure; a superseded one is not this person's claim any more; a closed one
    /// closing twice would write a second decision over the first. All three throw rather than
    /// no-op, because each is a caller bug about money and a silent no-op hides it.
    /// </summary>
    public void Close(
        CreditClosureReason reason,
        string note,
        string closedBy,
        DateTimeOffset now,
        Guid eventId)
    {
        if (ClosedAt is not null)
            throw new DomainException($"Credit {Id} is already closed.");
        if (ConsumedAt is not null)
            throw new DomainException(
                $"Credit {Id} was already paid by payout {ConsumedByPayoutId} and cannot be closed.");
        if (SupersededAt is not null)
            throw new DomainException($"Credit {Id} is superseded and is no longer owed to this payee.");
        if (string.IsNullOrWhiteSpace(note))
            throw new DomainException("A closure note is required.");
        if (note.Length > 1000)
            throw new DomainException("The closure note must not exceed 1000 characters.");
        if (string.IsNullOrWhiteSpace(closedBy))
            throw new DomainException("The closing actor is required.");

        ClosedAt = now;
        ClosedBy = closedBy;
        ClosureReason = reason;
        ClosureNote = note;

        RaiseDomainEvent(new CreditClosedEvent(
            eventId, now, Id, TransactionId, PayeeId, TenantId,
            reason, CreditedAmount.Amount, CreditedAmount.Currency, note));
    }

    // Undo consumption when a Paid payout is reverted. Returns this credit to the available pool.
    public void Unconsume(Guid eventId, DateTimeOffset now)
    {
        if (ConsumedAt is null)
            throw new DomainException($"Credit {Id} is not consumed and cannot be unconsumed.");

        var formerPayoutId = ConsumedByPayoutId!.Value;
        ConsumedAt = null;
        ConsumedByPayoutId = null;

        RaiseDomainEvent(new CreditUnconsumedEvent(eventId, now, Id, TransactionId, PayeeId, TenantId, formerPayoutId));
    }
}
