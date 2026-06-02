using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Events;
using Wasnie.Domain.Compensation.ValueObjects;

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
}
