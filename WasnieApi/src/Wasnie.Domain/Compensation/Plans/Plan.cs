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
    public PlanPeriodType? PeriodType { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    /// <summary>
    /// When this plan was archived. Null on every plan that has not been archived.
    ///
    /// WHY IT IS STORED. Archiving deactivates every assignment, so this date is the line between a
    /// sale that still pays through this plan and one that does not. It used to be reconstructible
    /// only from AuditLogs, and a purge of that table would have made the line uncomputable.
    ///
    /// IT NEVER GOES BACK TO NULL. Archiving is terminal — Activate() accepts Draft only — so nothing
    /// can un-archive a plan, and nothing may clear this. Append-only evidence, like the rest.
    /// </summary>
    public DateTimeOffset? ArchivedAt { get; private set; }

    // ── Clawback policy (opt-in per plan) ────────────────────────────────────
    // Both null on every existing plan, which is what keeps the clawback subsystem inert until a
    // tenant deliberately configures it: no maturation window means no proportional clawback.

    /// <summary>
    /// Days a deal must stay won before its commission is fully earned. A deal lost inside the
    /// window is clawed back proportionally: paid × (1 − DaysActive / MaturationDays).
    /// Null = this plan does not claw back churned deals.
    /// </summary>
    public int? ClawbackMaturationDays { get; private set; }

    /// <summary>
    /// Ceiling on how much of a period's commissions this plan lets a clawback withhold, in percent
    /// (0–100). The payee always takes home at least (100 − cap)% of what they earned, and the rest
    /// of the debt carries over. Null = no ceiling (the full payout may be withheld).
    /// </summary>
    public decimal? ClawbackCapPercent { get; private set; }

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
        Guid eventId,
        PlanPeriodType? periodType = null)
    {
        var plan = new Plan
        {
            Id = id,
            TenantId = tenantId,
            Name = name,
            Description = description,
            EffectivePeriod = effectivePeriod,
            Currency = currency,
            PeriodType = periodType,
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
        Floor? floor = null,
        DateRange? effectivePeriod = null,
        string? tag = null)
    {
        if (Status != PlanStatus.Draft)
        {
            throw new DomainException("Rules can only be modified on Draft plans.");
        }

        var rule = Rule.Create(Id, name, sortOrder, trigger ?? Trigger.Always(), measurement, rateTable, modifier, cap, floor, effectivePeriod: effectivePeriod, tag: tag);
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

    /// <returns>
    /// The rule the caller should now show. It is USUALLY the one identified by <paramref name="ruleId"/>
    /// — but not when that rule was stopped, in which case the edit lands on a new rule with a new Id.
    /// </returns>
    public Rule UpdateRule(
        Guid ruleId,
        string name,
        int sortOrder,
        Measurement measurement,
        RateTable rateTable,
        Trigger? trigger = null,
        Modifier? modifier = null,
        Cap? cap = null,
        Floor? floor = null,
        DateRange? effectivePeriod = null,
        string? tag = null)
    {
        if (Status != PlanStatus.Draft)
        {
            throw new DomainException("Rules can only be modified on Draft plans.");
        }

        // Stopped rules are findable here, unlike removed ones: a clone carries them into the Draft
        // precisely so they can be corrected, and a "not found" on the rule the screen is showing
        // would be a dead end.
        var rule = _rules.FirstOrDefault(r => r.Id == ruleId && (r.IsActive || r.IsStopped))
            ?? throw new DomainException($"Rule {ruleId} not found in this plan.");

        // ★★ EDITING A STOPPED RULE DOES NOT REVIVE IT — IT SUPERSEDES IT. Clearing the marker would
        // put StoppedAt back to null, which is the one thing this feature promises never happens,
        // and it is the exact shape of the bug Payee.Activate() left behind (clears DeactivatedAt,
        // erases the history). So the stopped rule stays stopped, with its date and its reason
        // intact, and the correction lands as a NEW rule beside it: "there is no unstop; there is a
        // new rule". The caller gets that new rule back, because its Id is not the one it asked for.
        //
        // Safe here and nowhere else: this path is Draft-only (guarded above), so neither rule has
        // ever been read by the engine.
        if (rule.IsStopped)
        {
            var replacement = Rule.Create(
                Id, name, sortOrder, trigger ?? Trigger.Always(), measurement, rateTable,
                modifier, cap, floor, effectivePeriod: effectivePeriod, tag: tag);

            _rules.Add(replacement);
            return replacement;
        }

        rule.Update(name, sortOrder, trigger ?? Trigger.Always(), measurement, rateTable, modifier, cap, floor, effectivePeriod: effectivePeriod, tag: tag);
        return rule;
    }

    /// <summary>
    /// THE EMERGENCY BRAKE. Stop one rule of a live plan from generating any further credit, without
    /// cloning the plan.
    ///
    /// ★★ WHY IT HAD TO EXIST. Until this method there was no way to stop a rule that pays wrong:
    /// RemoveRule and UpdateRule both demand Draft, and the only escape was cloning the whole plan —
    /// leaving assignments and quotas pointed at the old version. A commission engine with no brake
    /// means RevOps watches a miscalculation pay out until the end of the month.
    ///
    /// ★ THE PLAN DOES NOT CHANGE STATE, AND THE LAST RULE MAY GO TOO. An Active plan whose rules
    /// have all been stopped stays Active and keeps ingesting transactions — they are recorded and
    /// marked, never rejected, because the sale happened whatever the configuration says. "A plan
    /// with no live rules" is then DERIVED from the rules, not stored as a flag that can drift.
    /// </summary>
    /// <returns>The rule that was stopped, so the caller can report what it recorded.</returns>
    public Rule StopRule(Guid ruleId, string stoppedBy, string? reason, DateTimeOffset now)
    {
        if (Status != PlanStatus.Active)
        {
            throw new DomainCodedException(RuleStopInvariant.PlanNotActive, new Dictionary<string, object?>
            {
                ["status"] = Status.ToString(),
            });
        }

        // A rule already removed from a draft (!IsActive, no marker) is not stoppable — there is
        // nothing live about it — but an already-stopped one IS found, so Rule.Stop can answer with
        // AlreadyStopped and its date rather than a misleading "no such rule".
        var rule = _rules.FirstOrDefault(r => r.Id == ruleId && (r.IsActive || r.IsStopped))
            ?? throw new DomainCodedException(RuleStopInvariant.RuleNotFound, new Dictionary<string, object?>
            {
                ["ruleId"] = ruleId,
            });

        rule.Stop(stoppedBy, reason, now);

        UpdatedAt = now;
        UpdatedBy = stoppedBy;

        return rule;
    }

    /// <summary>
    /// Sets (or clears, with both nulls) the clawback policy.
    ///
    /// Allowed on Draft and Active plans on purpose — unlike rules, this is not part of the frozen
    /// calculation: every ledger entry stores the MaturationDays it was computed with, so changing
    /// the policy moves future clawbacks only and cannot rewrite a number already charged to a person.
    /// Archived plans are refused: nothing about a retired plan should still be tunable.
    /// </summary>
    public void SetClawbackPolicy(
        int? maturationDays, decimal? capPercent, string updatedBy, DateTimeOffset now)
    {
        if (Status == PlanStatus.Archived)
            throw new DomainException("Cannot change the clawback policy of an archived plan.");
        if (maturationDays is <= 0)
            throw new DomainException("Maturation days must be greater than zero.");
        if (capPercent is < 0m or > 100m)
            throw new DomainException("The clawback cap must be a percentage between 0 and 100.");
        if (string.IsNullOrWhiteSpace(updatedBy))
            throw new DomainException("UpdatedBy is required.");

        ClawbackMaturationDays = maturationDays;
        ClawbackCapPercent = capPercent;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
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
        ArchivedAt = now;
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
            PeriodType = PeriodType,
            Version = Version + 1,
            Status = PlanStatus.Draft,
            // The clawback policy travels with the version. Dropping it here turned a renewal into a
            // silent switch-off: the new version looked identical to its predecessor, was activated as
            // a routine renewal, and stopped recovering a single cent of unearned commission — with
            // nothing on screen or in the audit trail saying so. A new version inherits the previous
            // policy; turning the clawback off stays a deliberate act through SetClawbackPolicy.
            ClawbackMaturationDays = ClawbackMaturationDays,
            ClawbackCapPercent = ClawbackCapPercent,
            CreatedAt = now,
            CreatedBy = createdBy,
            UpdatedAt = now,
            UpdatedBy = createdBy
        };

        // ★ STOPPED RULES TRAVEL, REMOVED ONES DO NOT — and the difference is the whole reason the
        // marker is a separate field. Dropping a stopped rule here would hand the next version a
        // clean slate that quietly omits the rule someone braked, so the next reader rebuilds it
        // from scratch not knowing it had ever paid wrong. It arrives STILL STOPPED, carrying the
        // original date, actor and reason: a clone is not a review, and nothing here decides that
        // the problem was fixed. Correcting it in the new Draft is a deliberate, separate act.
        foreach (var rule in _rules.Where(r => r.IsActive || r.IsStopped))
        {
            var copy = Rule.Create(
                clone.Id,
                rule.Name,
                rule.SortOrder,
                rule.Trigger,
                rule.Measurement,
                rule.RateTable,
                rule.Modifier,
                rule.Cap,
                rule.Floor,
                id: newId(),
                effectivePeriod: rule.EffectivePeriod,
                tag: rule.Tag);

            copy.CopyStopMarkerFrom(rule);
            clone._rules.Add(copy);
        }

        clone.RaiseDomainEvent(new PlanVersionClonedEvent(
            newId(), now, Id, clone.Id, clone.Version, TenantId));

        return clone;
    }
}
