namespace Wasnie.Application.Models.Imports;

public sealed class PayeeImportColumnMapping
{
    // FullNameColumns: multi-column composition (e.g. First Name + Last Name).
    // Supersedes FullNameColumn when populated; kept for backward compat.
    public string[] FullNameColumns { get; init; } = [];
    public string FullNameColumn { get; init; } = string.Empty;
    public required string EmployeeCodeColumn { get; init; }
    public required string EmailColumn { get; init; }
    public required string HireDateColumn { get; init; }
    public string? RoleColumn { get; init; }
    public string? ManagerEmployeeCodeColumn { get; init; }
    public string? EmploymentTypeColumn { get; init; }
    public string? LocationColumn { get; init; }
}
