using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Plans;

public sealed class Rule : Entity
{
    public Guid PlanId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public Trigger Trigger { get; private set; } = Trigger.Always();
    public Measurement Measurement { get; private set; } = new();
    public RateTable RateTable { get; private set; } = RateTable.Flat(0m);
    public Modifier? Modifier { get; private set; }
    public Cap? Cap { get; private set; }
    public Floor? Floor { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateRange? EffectivePeriod { get; private set; }
    public string? Tag { get; private set; }

    /// <summary>
    /// When the emergency brake was pulled on this rule, or null if it never was.
    ///
    /// ★★ THIS EXISTS BECAUSE <see cref="IsActive"/> ALREADY MEANT SOMETHING ELSE. A false
    /// <c>IsActive</c> has always meant "removed from a draft that was never activated" — three
    /// places read it that way (the clone, the plan detail, <c>Plan.UpdateRule</c>) — and a rule
    /// switched off mid-flight on a live plan is not that. Overloading the flag would have made all
    /// three lie at once: the clone would drop the stopped rule, the detail screen would hide it,
    /// and nobody could see that a plan had been braked. So the fact travels in its own field and
    /// <c>IsActive</c> keeps its meaning; the two together say which of the two things happened
    /// (<c>!IsActive &amp;&amp; StoppedAt == null</c> is removed, <c>StoppedAt != null</c> is stopped).
    ///
    /// ★ IT NEVER GOES BACK TO NULL. Not through a public method, not through an internal one. The
    /// precedent this deliberately does not repeat is <c>Payee.Activate()</c>, which clears
    /// <c>DeactivatedAt</c> and erases the history in a product built on an append-only ledger.
    /// Correcting a stopped rule produces a NEW rule beside it — see <c>Plan.UpdateRule</c>.
    /// </summary>
    public DateTimeOffset? StoppedAt { get; private set; }

    /// <summary>Who pulled the brake. Null exactly when <see cref="StoppedAt"/> is.</summary>
    public string? StoppedBy { get; private set; }

    /// <summary>
    /// Why. Mandatory, and kept ON THE RULE rather than only in <c>AuditLogs</c>: the audit table is
    /// not a surface anyone reads, and the screen that shows a stopped rule has to be able to say
    /// what happened without a second query into an append-only log.
    /// </summary>
    public string? StopReason { get; private set; }

    /// <summary>Derived, never stored — the flag is what drifts, the fact is what does not.</summary>
    public bool IsStopped => StoppedAt is not null;

    /// <summary>The longest reason the column holds. Echoed to the client with the refusal.</summary>
    public const int StopReasonMaxLength = 500;

    private Rule() { }

    private static void ValidateTag(string? tag)
    {
        if (tag != null && tag.Trim().Length > 50)
            throw new DomainException("Rule tag must not exceed 50 characters.");
    }

    // Units measurement calculates commission as ratePerUnit × quantity.
    // Tiered/Attainment require an amount base — incompatible with per-unit math in this version.
    private static void ValidateMeasurementRateTableCompatibility(Measurement measurement, RateTable rateTable)
    {
        if (measurement.Type == MeasurementType.Units && rateTable.Type != RateTableType.Flat)
            throw new DomainException(
                "Units measurement only supports a Flat rate table. " +
                "Tiered and Attainment rate tables are not supported for unit-based commission.");
    }

    internal static Rule Create(
        Guid planId,
        string name,
        int sortOrder,
        Trigger trigger,
        Measurement measurement,
        RateTable rateTable,
        Modifier? modifier,
        Cap? cap,
        Floor? floor,
        Guid id = default,
        DateRange? effectivePeriod = null,
        string? tag = null)
    {
        ValidateTag(tag);
        ValidateMeasurementRateTableCompatibility(measurement, rateTable);

        return new()
        {
            Id = id == default ? Guid.NewGuid() : id,
            PlanId = planId,
            Name = name,
            SortOrder = sortOrder,
            Trigger = trigger,
            Measurement = measurement,
            RateTable = rateTable,
            Modifier = modifier,
            Cap = cap,
            Floor = floor,
            EffectivePeriod = effectivePeriod,
            Tag = tag
        };
    }

    internal void Update(
        string name,
        int sortOrder,
        Trigger trigger,
        Measurement measurement,
        RateTable rateTable,
        Modifier? modifier,
        Cap? cap,
        Floor? floor,
        DateRange? effectivePeriod = null,
        string? tag = null)
    {
        ValidateTag(tag);
        ValidateMeasurementRateTableCompatibility(measurement, rateTable);

        Name = name;
        SortOrder = sortOrder;
        Trigger = trigger;
        Measurement = measurement;
        RateTable = rateTable;
        Modifier = modifier;
        Cap = cap;
        Floor = floor;
        EffectivePeriod = effectivePeriod;
        Tag = tag;
    }

    internal void Deactivate() => IsActive = false;

    /// <summary>
    /// Pull the emergency brake: this rule stops generating credits from now on.
    ///
    /// ★ IT SETS <see cref="IsActive"/> TO FALSE TOO, AND THAT IS THE WHOLE MECHANISM. The engine
    /// filters on <c>IsActive</c> (<c>CreditAllocationService.cs:332</c>) and nothing else; a marker
    /// the engine does not read would be a screen that says "stopped" over a rule that keeps paying.
    /// The three fields are what tell the READERS which kind of inactive this is.
    ///
    /// ★ IT TOUCHES NO MONEY. Credits already generated by this rule keep their amount and their
    /// status. Stopping is about the next transaction, never about the last one — undoing what was
    /// already paid is a clawback, a different act with a different audit trail.
    ///
    /// ★ NO MATCHING <c>Resume</c> EXISTS, ON PURPOSE. See <see cref="StoppedAt"/>.
    /// </summary>
    internal void Stop(string stoppedBy, string? reason, DateTimeOffset now)
    {
        // Checked first: someone whose brake is already pulled should be told that, not sent back to
        // the form for a reason that would be discarded anyway.
        if (StoppedAt is not null)
        {
            throw new DomainCodedException(RuleStopInvariant.AlreadyStopped, new Dictionary<string, object?>
            {
                ["stoppedAt"] = StoppedAt,
                ["stoppedBy"] = StoppedBy,
            });
        }

        var trimmed = reason?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new DomainCodedException(RuleStopInvariant.ReasonRequired);
        }

        if (trimmed.Length > StopReasonMaxLength)
        {
            throw new DomainCodedException(RuleStopInvariant.ReasonTooLong, new Dictionary<string, object?>
            {
                ["maxLength"] = StopReasonMaxLength,
                ["actualLength"] = trimmed.Length,
            });
        }

        StoppedAt = now;
        StoppedBy = stoppedBy;
        StopReason = trimmed;
        IsActive = false;
    }

    /// <summary>
    /// Reproduce an existing rule's stop marker on THIS rule, used only when a plan version is
    /// cloned. Separate from <see cref="Stop"/> because a clone copies a fact that already happened
    /// — the original date, actor and reason — rather than recording a new one at clone time.
    /// </summary>
    internal void CopyStopMarkerFrom(Rule source)
    {
        if (source.StoppedAt is null)
        {
            return;
        }

        StoppedAt = source.StoppedAt;
        StoppedBy = source.StoppedBy;
        StopReason = source.StopReason;
        IsActive = false;
    }
}
