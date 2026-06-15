using Microsoft.AspNetCore.Identity;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Infrastructure.Identity;

public sealed class IdentityService(
    UserManager<IdentityUser> userManager,
    SignInManager<IdentityUser> signInManager)
    : IIdentityService
{
    public async Task<(bool Succeeded, string? UserId, IList<string> Errors)> CreateUserAsync(
        string email,
        string password,
        IList<string> roles,
        IDictionary<string, string> claims)
    {
        var user = new IdentityUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = false,
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            return (false, null, createResult.Errors.Select(e => e.Description).ToList());
        }

        foreach (var role in roles)
        {
            await userManager.AddToRoleAsync(user, role);
        }

        foreach (var (type, value) in claims)
        {
            await userManager.AddClaimAsync(user, new System.Security.Claims.Claim(type, value));
        }

        return (true, user.Id, []);
    }

    public async Task<(bool Succeeded, string? UserId, string? Email)> ValidateCredentialsAsync(
        string email,
        string password)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return (false, null, null);
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return (false, null, null);
        }

        return (true, user.Id, user.Email);
    }

    public async Task<string?> FindUserIdByEmailAsync(string email)
    {
        var user = await userManager.FindByEmailAsync(email);
        return user?.Id;
    }

    public async Task<string?> FindEmailByUserIdAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        return user?.Email;
    }

    public async Task<IList<string>> GetUserRolesAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return [];
        }

        return await userManager.GetRolesAsync(user);
    }

    public async Task<string?> GetTenantIdClaimAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return null;
        var claims = await userManager.GetClaimsAsync(user);
        return claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value;
    }

    public async Task<bool> IsEmailConfirmedAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        return user?.EmailConfirmed ?? false;
    }

    public async Task<string?> GetClaimAsync(string userId, string claimType)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return null;
        var claims = await userManager.GetClaimsAsync(user);
        return claims.FirstOrDefault(c => c.Type == claimType)?.Value;
    }

    public async Task<bool> SetEmailConfirmedAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return false;
        user.EmailConfirmed = true;
        var result = await userManager.UpdateAsync(user);
        return result.Succeeded;
    }

    public async Task<bool> ResetPasswordAsync(string userId, string newPassword)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return false;

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        return result.Succeeded;
    }

    public async Task<bool> VerifyPasswordAsync(string userId, string password)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return false;
        return await userManager.CheckPasswordAsync(user, password);
    }

    public async Task<(bool Succeeded, IList<string> Errors)> ChangePasswordAsync(
        string userId,
        string currentPassword,
        string newPassword)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return (false, ["User not found."]);

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
            return (false, result.Errors.Select(e => e.Description).ToList());

        return (true, []);
    }

    public async Task<bool> UpdateClaimAsync(string userId, string claimType, string newValue)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return false;

        var claims = await userManager.GetClaimsAsync(user);
        var existing = claims.FirstOrDefault(c => c.Type == claimType);

        IdentityResult result;
        if (existing is not null)
        {
            result = await userManager.ReplaceClaimAsync(
                user,
                existing,
                new System.Security.Claims.Claim(claimType, newValue));
        }
        else
        {
            result = await userManager.AddClaimAsync(
                user,
                new System.Security.Claims.Claim(claimType, newValue));
        }

        return result.Succeeded;
    }

    public async Task<bool> ChangeEmailAsync(string userId, string newEmail)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return false;

        var token = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        var result = await userManager.ChangeEmailAsync(user, newEmail, token);
        if (!result.Succeeded) return false;

        // Keep UserName in sync with Email (ASP.NET Identity pattern).
        user.UserName = newEmail;
        var updateResult = await userManager.UpdateAsync(user);
        return updateResult.Succeeded;
    }
}
