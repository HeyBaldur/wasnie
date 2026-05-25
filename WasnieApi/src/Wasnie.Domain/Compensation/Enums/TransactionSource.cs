namespace Wasnie.Domain.Compensation.Enums;

public enum TransactionSource
{
    Manual = 0,
    CrmSync = 1,
    EtlImport = 2,
    ApiIngestion = 3
}
