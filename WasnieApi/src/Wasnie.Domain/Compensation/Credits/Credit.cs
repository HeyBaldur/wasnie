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
}
