namespace Wasnie.Application.Common.Constants;

/// <summary>
/// Canonical field names for the FieldRequirementSettings catalog.
/// Use these constants in validators and import services — never raw string literals.
/// </summary>
public static class PayeeFieldNames
{
    public const string Entity       = "Payee";
    public const string Email          = "Email";
    public const string HireDate       = "HireDate";
    public const string Role           = "Role";
    public const string ManagerId      = "ManagerId";
    public const string EmploymentType = "EmploymentType";
    public const string Location       = "Location";
}
