using Wasnie.Application.Models.Imports;

namespace Wasnie.Application.Services.Imports;

public interface ITransactionUpdateValidationService
{
    Task<List<TransactionUpdateRowPreviewResult>> ValidateAsync(
        List<Dictionary<string, string>> rows,
        TransactionUpdateColumnMapping mapping,
        CancellationToken ct = default);
}
