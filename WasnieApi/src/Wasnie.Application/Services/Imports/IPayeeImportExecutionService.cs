using Wasnie.Application.Models.Imports;

namespace Wasnie.Application.Services.Imports;

public interface IPayeeImportExecutionService
{
    Task<PayeeImportResult> ExecuteAsync(
        List<Dictionary<string, string>> rows,
        PayeeImportColumnMapping mapping,
        List<PayeeRowValidationResult> validationResults,
        bool skipRowsWithWarnings,
        string importedBy,
        string originalFileName,
        CancellationToken cancellationToken = default);
}
