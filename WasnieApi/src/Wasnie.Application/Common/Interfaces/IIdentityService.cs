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

    // ── KAN-32, user administration ──────────────────────────────────────────

    /// <summary>
    /// Puts the user in exactly the roles given, removing every other one.
    ///
    /// ★ REPLACE, NOT ADD. A role change that only added would quietly leave the old authority in
    /// place, so demoting somebody would grant nothing and take nothing away — the most dangerous
    /// shape this operation could have.
    /// </summary>
    Task<(bool Succeeded, IList<string> Errors)> ReplaceUserRolesAsync(string userId, IList<string> roles);

    /// <summary>
    /// Email, first name, last name and role for each id, in one round trip.
    ///
    /// ★ ONE CALL FOR THE WHOLE PAGE. The users list needs all four for every row, and asking per row
    /// is how a twenty-person tenant turns into eighty queries.
    /// </summary>
    Task<IReadOnlyList<IdentityUserSummary>> GetUserSummariesAsync(IReadOnlyCollection<string> userIds);

    /// <summary>
    /// Every user id carrying this tenant's <c>tenant_id</c> claim.
    ///
    /// ★★ IDENTITY IS ASKED, NOT TenantUsers, AND THE ORDER MATTERS. Membership lives in the claim —
    /// that is what a sign-in reads — while TenantUsers records access decisions. Tenants created
    /// before KAN-32 have the claim and no access row, so a roster built from the access table would
    /// be empty for every customer that exists today.
    /// </summary>
    Task<IReadOnlyList<string>> GetTenantUserIdsAsync(string tenantId);

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

/// <summary>What the users screen needs about a person, gathered from Identity's tables.</summary>
public sealed record IdentityUserSummary(
    string UserId,
    string Email,
    string? FirstName,
    string? LastName,
    string? Role,
    bool EmailConfirmed);
