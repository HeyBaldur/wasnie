using Wasnie.Application.Models.Imports;

namespace Wasnie.Application.Services.Imports;

public interface IPayeeImportValidationService
{
    Task<List<PayeeRowValidationResult>> ValidateAsync(
        List<Dictionary<string, string>> rows,
        PayeeImportColumnMapping mapping,
        CancellationToken cancellationToken = default);
}
