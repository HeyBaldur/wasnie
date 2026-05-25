using Wasnie.Domain.Common;

namespace Wasnie.Domain.Entities;

public sealed class Payee : BaseAuditableEntity
{
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string EmployeeCode { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    private Payee() { }

    public static Payee Create(
        Guid tenantId,
        string firstName,
        string lastName,
        string email,
        string employeeCode,
        string createdBy)
    {
        return new Payee
        {
            TenantId = tenantId,
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            EmployeeCode = employeeCode,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = createdBy
        };
    }

    public string FullName => $"{FirstName} {LastName}";
}
