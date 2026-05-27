using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Events;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Plans;

public sealed class Plan : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public int Version { get; private set; } = 1;
    public PlanStatus Status { get; private set; } = PlanStatus.Draft;
    public DateRange EffectivePeriod { get; private set; } = null!;
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private readonly List<Rule> _rules = [];
    public IReadOnlyList<Rule> Rules => _rules.AsReadOnly();

    private Plan() { }

    public static Plan Create(
        Guid tenantId,
        string name,
        string description,
        DateRange effectivePeriod,
        string currency,
        string createdBy,
        Guid id,
        DateTimeOffset now,
        Guid eventId)
    {
        var plan = new Plan
        {
            Id = id,
            TenantId = tenantId,
            Name = name,
            Description = description,
            EffectivePeriod = effectivePeriod,
            Currency = currency,
            Version = 1,
            Status = PlanStatus.Draft,
            CreatedAt = now,
            CreatedBy = createdBy,
            UpdatedAt = now,
            UpdatedBy = createdBy
        };

        plan.RaiseDomainEvent(new PlanCreatedEvent(eventId, now, plan.Id, tenantId));
        return plan;
    }

    public Rule AddRule(
        string name,
        int sortOrder,
        Measurement measurement,
        RateTable rateTable,
        Trigger? trigger = null,
        Modifier? modifier = null,
        Cap? cap = null,
        Floor? floor = null)
    {
        if (Status != PlanStatus.Draft)
        {
            throw new DomainException("Rules can only be modified on Draft plans.");
        }

        var rule = Rule.Create(Id, name, sortOrder, trigger ?? Trigger.Always(), measurement, rateTable, modifier, cap, floor);
        _rules.Add(rule);
        return rule;
    }

    public void RemoveRule(Guid ruleId)
    {
        if (Status != PlanStatus.Draft)
        {
            throw new DomainException("Rules can only be modified on Draft plans.");
        }

        var rule = _rules.FirstOrDefault(r => r.Id == ruleId)
            ?? throw new DomainException($"Rule {ruleId} not found in this plan.");

        rule.Deactivate();
    }

    public void UpdateRule(
        Guid ruleId,
        string name,
        int sortOrder,
        Measurement measurement,
        RateTable rateTable,
        Trigger? trigger = null,
        Modifier? modifier = null,
        Cap? cap = null,
        Floor? floor = null)
    {
        if (Status != PlanStatus.Draft)
        {
            throw new DomainException("Rules can only be modified on Draft plans.");
        }

        var rule = _rules.FirstOrDefault(r => r.Id == ruleId && r.IsActive)
            ?? throw new DomainException($"Rule {ruleId} not found in this plan.");

        rule.Update(name, sortOrder, trigger ?? Trigger.Always(), measurement, rateTable, modifier, cap, floor);
    }

    public void Activate(string updatedBy, DateTimeOffset now, Guid eventId)
    {
        if (Status != PlanStatus.Draft)
        {
            throw new DomainException("Only Draft plans can be activated.");
        }

        if (!_rules.Any(r => r.IsActive))
        {
            throw new DomainException("A plan must have at least one active rule before activation.");
        }

        Status = PlanStatus.Active;
        UpdatedAt = now;
        UpdatedBy = updatedBy;

        RaiseDomainEvent(new PlanActivatedEvent(eventId, now, Id, TenantId));
    }

    public void Archive(string updatedBy, DateTimeOffset now, Guid eventId)
    {
        if (Status != PlanStatus.Active)
        {
            throw new DomainException("Only Active plans can be archived.");
        }

        Status = PlanStatus.Archived;
        UpdatedAt = now;
        UpdatedBy = updatedBy;

        RaiseDomainEvent(new PlanArchivedEvent(eventId, now, Id, TenantId));
    }

    public void CheckDeletable()
    {
        if (Status != PlanStatus.Draft)
            throw new DomainException("Only Draft plans can be deleted.");

        if (_rules.Any(r => r.IsActive))
            throw new DomainException("Draft plans with active rules cannot be deleted. Archive the plan instead.");
    }

    public Plan CloneAsNewVersion(string createdBy, DateTimeOffset now, Func<Guid> newId)
    {
        if (Status == PlanStatus.Draft)
        {
            throw new DomainException("Cannot clone a Draft plan. Edit it directly instead.");
        }

        var clone = new Plan
        {
            Id = newId(),
            TenantId = TenantId,
            Name = Name,
            Description = Description,
            EffectivePeriod = DateRange.Of(EffectivePeriod.Start, EffectivePeriod.End),
            Currency = Currency,
            Version = Version + 1,
            Status = PlanStatus.Draft,
            CreatedAt = now,
            CreatedBy = createdBy,
            UpdatedAt = now,
            UpdatedBy = createdBy
        };

        foreach (var rule in _rules.Where(r => r.IsActive))
        {
            clone._rules.Add(Rule.Create(
                clone.Id,
                rule.Name,
                rule.SortOrder,
                rule.Trigger,
                rule.Measurement,
                rule.RateTable,
                rule.Modifier,
                rule.Cap,
                rule.Floor,
                id: newId()));
        }

        clone.RaiseDomainEvent(new PlanVersionClonedEvent(
            newId(), now, Id, clone.Id, clone.Version, TenantId));

        return clone;
    }
}
