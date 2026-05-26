using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Events;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Assignments;

public sealed class PlanAssignment : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid PayeeId { get; private set; }
    public PayeeReference PayeeSnapshot { get; private set; } = null!;
    public DateRange EffectivePeriod { get; private set; } = null!;
    public AssignmentStatus Status { get; private set; } = AssignmentStatus.Active;
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private PlanAssignment() { }

    public string? Notes { get; private set; }

    public static PlanAssignment Create(
        Guid tenantId,
        Guid planId,
        Guid payeeId,
        PayeeReference payeeSnapshot,
        DateRange effectivePeriod,
        string createdBy,
        string? notes = null)
    {
        var assignment = new PlanAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PlanId = planId,
            PayeeId = payeeId,
            PayeeSnapshot = payeeSnapshot,
            EffectivePeriod = effectivePeriod,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            Status = AssignmentStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = createdBy,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = createdBy
        };

        assignment.RaiseDomainEvent(new PlanAssignmentActivatedEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, assignment.Id, planId, payeeId, tenantId));

        return assignment;
    }

    public void UpdateNotes(string? notes, string updatedBy)
    {
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedBy = updatedBy;
    }

    public void Deactivate(string updatedBy)
    {
        if (Status == AssignmentStatus.Deactivated)
        {
            throw new DomainException("Assignment is already deactivated.");
        }

        Status = AssignmentStatus.Deactivated;
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedBy = updatedBy;

        RaiseDomainEvent(new PlanAssignmentDeactivatedEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Id, TenantId));
    }
}
