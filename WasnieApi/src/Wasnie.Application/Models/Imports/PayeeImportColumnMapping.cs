namespace Wasnie.Application.Models.Imports;

public sealed class PayeeImportColumnMapping
{
    // FullNameColumns: multi-column composition (e.g. First Name + Last Name).
    // Supersedes FullNameColumn when populated; kept for backward compat.
    public string[] FullNameColumns { get; init; } = [];
    public string FullNameColumn { get; init; } = string.Empty;
    public required string EmployeeCodeColumn { get; init; }
    // Optional for the same reason as HireDateColumn: Email's requiredness is tenant-configurable,
    // so a tenant with it set to Optional must be able to import a file with no email column.
    public string? EmailColumn { get; init; }
    // Optional: hire date is informative only and its requirement is driven per tenant by
    // FieldRequirementSettings('Payee','HireDate'). A tenant with it set to Optional must be
    // able to import a file that has no hire date column at all, so this cannot be `required`.
    public string? HireDateColumn { get; init; }
    public string? RoleColumn { get; init; }
    public string? ManagerEmployeeCodeColumn { get; init; }
    public string? EmploymentTypeColumn { get; init; }
    public string? LocationColumn { get; init; }
}
