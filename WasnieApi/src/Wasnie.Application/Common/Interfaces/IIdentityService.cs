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

    Task<bool> IsEmailConfirmedAsync(string userId);

    Task<string?> GetClaimAsync(string userId, string claimType);

    Task<bool> SetEmailConfirmedAsync(string userId);

    Task<bool> ResetPasswordAsync(string userId, string newPassword);

    Task<bool> VerifyPasswordAsync(string userId, string password);

    Task<(bool Succeeded, IList<string> Errors)> ChangePasswordAsync(
        string userId,
        string currentPassword,
        string newPassword);

    Task<bool> UpdateClaimAsync(string userId, string claimType, string newValue);

    Task<bool> ChangeEmailAsync(string userId, string newEmail);

    // Two-factor authentication (TOTP via ASP.NET Identity built-in)
    Task<bool> IsTwoFactorEnabledAsync(string userId);
    Task<string?> GetOrCreateTotpSecretAsync(string userId);
    Task<bool> VerifyTotpCodeAsync(string userId, string code);
    Task<(bool Succeeded, IEnumerable<string> RecoveryCodes)> EnableTwoFactorAsync(string userId, string verificationCode);
    Task<bool> DisableTwoFactorAsync(string userId);
    Task<IEnumerable<string>?> GenerateRecoveryCodesAsync(string userId, int count);
    Task<bool> RedeemRecoveryCodeAsync(string userId, string code);
    Task<int> CountRecoveryCodesAsync(string userId);
}
