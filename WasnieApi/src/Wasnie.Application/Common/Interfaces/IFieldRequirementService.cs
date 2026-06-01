namespace Wasnie.Application.Common.Interfaces;

public interface IFieldRequirementService
{
    Task<bool> IsRequiredAsync(string entityName, string fieldName, CancellationToken cancellationToken = default);
}
