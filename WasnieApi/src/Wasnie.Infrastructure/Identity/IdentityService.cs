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
}
