namespace Wasnie.Application.Common.Interfaces;

public interface IIdentityService
{
    Task<(bool Succeeded, string? UserId, IList<string> Errors)> CreateUserAsync(
        string email,
        string password,
        IList<string> roles,
        IDictionary<string, string> claims);

    Task<(bool Succeeded, string? UserId, string? Email)> ValidateCredentialsAsync(
        string email,
        string password);

    Task<string?> FindUserIdByEmailAsync(string email);

    Task<string?> FindEmailByUserIdAsync(string userId);

    Task<IList<string>> GetUserRolesAsync(string userId);

    Task<string?> GetTenantIdClaimAsync(string userId);
}
