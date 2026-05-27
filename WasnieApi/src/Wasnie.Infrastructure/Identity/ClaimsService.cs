using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Infrastructure.Identity;

public sealed class ClaimsService(IHttpContextAccessor httpContextAccessor) : IClaimsService
{
    public string? GetRole() =>
        httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role);
}
