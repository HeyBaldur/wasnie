using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.ValueObjects;

public sealed class PayeeReference : ValueObject
{
    public Guid PayeeId { get; }
    public string FullName { get; }
    public string EmployeeCode { get; }

    private PayeeReference(Guid payeeId, string fullName, string employeeCode)
    {
        PayeeId = payeeId;
        FullName = fullName;
        EmployeeCode = employeeCode;
    }

    public static PayeeReference Snapshot(Guid payeeId, string fullName, string employeeCode) =>
        new(payeeId, fullName, employeeCode);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return PayeeId;
        yield return FullName;
        yield return EmployeeCode;
    }
}
