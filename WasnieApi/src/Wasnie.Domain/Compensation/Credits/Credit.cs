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
    // Optimistic concurrency token — EF uses this to detect concurrent consumption of the same credit.
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
        Guid eventId)
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
            AllocatedBy = allocatedBy
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
