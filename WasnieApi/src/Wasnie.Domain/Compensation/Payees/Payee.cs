using Wasnie.Domain.Common;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Payees;

public sealed class Payee : BaseAuditableEntity
{
    public string FullName { get; private set; } = string.Empty;
    public string EmployeeCode { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Role { get; private set; }
    public Guid? ManagerId { get; private set; }
    public DateOnly? HireDate { get; private set; }
    public DateOnly? TerminationDate { get; private set; }
    public PayeeStatus Status { get; private set; } = PayeeStatus.Active;

    private Payee() { }

    public static Payee Create(
        Guid tenantId,
        string fullName,
        string employeeCode,
        string? email,
        DateOnly? hireDate,
        string createdBy,
        Guid id,
        DateTimeOffset now,
        string? role = null,
        Guid? managerId = null)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("Full name is required.");
        if (string.IsNullOrWhiteSpace(employeeCode))
            throw new DomainException("Employee code is required.");
        if (hireDate.HasValue && hireDate.Value > DateOnly.FromDateTime(now.UtcDateTime))
            throw new DomainException("Hire date cannot be in the future.");

        return new Payee
        {
            Id = id,
            TenantId = tenantId,
            FullName = fullName.Trim(),
            EmployeeCode = employeeCode.Trim(),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant(),
            Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim(),
            ManagerId = managerId,
            HireDate = hireDate,
            Status = PayeeStatus.Active,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedBy = createdBy
        };
    }

    public void Update(
        string fullName,
        string employeeCode,
        string? email,
        DateOnly? hireDate,
        string? role,
        Guid? managerId,
        string updatedBy,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("Full name is required.");
        if (string.IsNullOrWhiteSpace(employeeCode))
            throw new DomainException("Employee code is required.");
        if (managerId == Id)
            throw new DomainException("A payee cannot be their own manager.");

        FullName = fullName.Trim();
        EmployeeCode = employeeCode.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim();
        ManagerId = managerId;
        HireDate = hireDate;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    public void MarkAsActive(string updatedBy, DateTimeOffset now)
    {
        if (Status == PayeeStatus.Active) return;

        Status = PayeeStatus.Active;
        TerminationDate = null;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    public void MarkAsOnLeave(string updatedBy, DateTimeOffset now)
    {
        if (Status == PayeeStatus.Terminated)
            throw new DomainException("Terminated payees cannot be set to On Leave directly. Mark as Active first.");
        if (Status == PayeeStatus.OnLeave) return;

        Status = PayeeStatus.OnLeave;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    public void MarkAsTerminated(DateOnly terminationDate, string updatedBy, DateTimeOffset now)
    {
        if (Status == PayeeStatus.Terminated) return;

        Status = PayeeStatus.Terminated;
        TerminationDate = terminationDate;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }
}
