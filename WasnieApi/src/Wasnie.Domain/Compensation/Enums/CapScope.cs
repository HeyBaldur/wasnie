namespace Wasnie.Domain.Compensation.Enums;

public enum CapScope
{
    // Defined but not yet honored by the engine; rejected at validation until implemented. See WI CapScope.
    PerPeriod = 0,
    PerTransaction = 1,
    // Defined but not yet honored by the engine; rejected at validation until implemented. See WI CapScope.
    Total = 2
}
