using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Events;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Quotas;

public sealed class Quota : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid PayeeId { get; private set; }
    public Guid PlanId { get; private set; }
    public Money Amount { get; private set; } = null!;
    public DateRange Period { get; private set; } = null!;
    public QuotaStatus Status { get; private set; } = QuotaStatus.Draft;
    public QuotaMeasurementType MeasurementType { get; private set; } = QuotaMeasurementType.Revenue;
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private Quota() { }

    public static Quota Create(
        Guid tenantId,
        Guid payeeId,
        Guid planId,
        Money amount,
        DateRange period,
        QuotaMeasurementType measurementType,
        string createdBy,
        string? notes = null)
    {
        if (notes is not null && notes.Length > 500)
            throw new DomainException("Notes must not exceed 500 characters.");

        return new Quota
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PayeeId = payeeId,
            PlanId = planId,
            Amount = amount,
            Period = period,
            MeasurementType = measurementType,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            Status = QuotaStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = createdBy,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = createdBy
        };
    }

    public void UpdateDraft(Money amount, DateRange period, QuotaMeasurementType measurementType, string? notes, string updatedBy)
    {
        if (Status != QuotaStatus.Draft)
            throw new DomainException("Only Draft quotas can be updated.");
        if (notes is not null && notes.Length > 500)
            throw new DomainException("Notes must not exceed 500 characters.");

        Amount = amount;
        Period = period;
        MeasurementType = measurementType;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedBy = updatedBy;
    }

    public void Activate(string updatedBy)
    {
        if (Status != QuotaStatus.Draft)
        {
            throw new DomainException("Only Draft quotas can be activated.");
        }

        Status = QuotaStatus.Active;
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedBy = updatedBy;

        RaiseDomainEvent(new QuotaActivatedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Id, TenantId));
    }

    public void Close(string updatedBy)
    {
        if (Status != QuotaStatus.Active)
        {
            throw new DomainException("Only Active quotas can be closed.");
        }

        Status = QuotaStatus.Closed;
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedBy = updatedBy;

        RaiseDomainEvent(new QuotaClosedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Id, TenantId));
    }
}
