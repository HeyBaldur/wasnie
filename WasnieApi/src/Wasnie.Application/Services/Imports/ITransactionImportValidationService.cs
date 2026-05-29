using Wasnie.Application.Models.Imports;

namespace Wasnie.Application.Services.Imports;

public interface ITransactionImportValidationService
{
    Task<List<TransactionRowValidationResult>> ValidateAsync(
        List<Dictionary<string, string>> rows,
        TransactionImportColumnMapping mapping,
        CancellationToken ct = default);
}
