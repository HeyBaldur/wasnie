using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.Application.Features.Auth.Mappings;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Entities;
using Wasnie.Domain.Identity;

namespace Wasnie.Application.Features.Auth.Handlers;

public sealed class RegisterTenantCommandHandler(
    IApplicationDbContext dbContext,
    IIdentityService identityService,
    ITokenService tokenService,
    IEmailService emailService,
    IAuditService auditService,
    IOptions<ResendOptions> resendOptions,
    IClock clock,
    IGuidGenerator guid,
    ILogger<RegisterTenantCommandHandler> logger)
    : IRequestHandler<RegisterTenantCommand, Result<AuthResultDto>>
{
    public async Task<Result<AuthResultDto>> Handle(
        RegisterTenantCommand request,
        CancellationToken cancellationToken)
    {
        var slugTaken = dbContext.Tenants.Any(t => t.Slug == request.TenantSlug);
        if (slugTaken)
            return Result<AuthResultDto>.Failure("Tenant slug is already taken.");

        var tenant = Tenant.Create(request.TenantName, request.TenantSlug, guid.NewGuid(), clock.UtcNowOffset);
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync(cancellationToken);

        var (succeeded, userId, errors) = await identityService.CreateUserAsync(
            request.AdminEmail,
            request.AdminPassword,
            ["TenantAdmin"],
            new Dictionary<string, string>
            {
                ["tenant_id"] = tenant.Id.ToString(),
                ["given_name"] = request.AdminFirstName,
                ["family_name"] = request.AdminLastName,
            });

        if (!succeeded || userId is null)
            return Result<AuthResultDto>.Failure(string.Join("; ", errors));

        // Generate and persist confirmation token (48h expiry).
        var now = clock.UtcNowOffset;
        var (rawToken, tokenHash) = GenerateToken();
        dbContext.EmailConfirmationTokens.Add(
            EmailConfirmationToken.Create(guid.NewGuid(), userId, tokenHash, now.AddHours(48), now));
        await dbContext.SaveChangesAsync(cancellationToken);

        // Send confirmation email. Log failure but don't abort registration.
        var confirmUrl = BuildConfirmUrl(resendOptions.Value.FrontendBaseUrl, userId, rawToken);
        try
        {
            await emailService.SendEmailConfirmationAsync(
                request.AdminEmail, request.AdminFirstName, confirmUrl, "en", cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not send confirmation email to {Email}", request.AdminEmail);
        }

        // In dev: always log the token so the founder can confirm without checking email.
        logger.LogInformation(
            "[DEV] Email confirmation link for {Email}: {Url}", request.AdminEmail, confirmUrl);

        var roles = await identityService.GetUserRolesAsync(userId);
        var tokens = await tokenService.GenerateTokenPairAsync(userId, request.AdminEmail, tenant.Id, roles);

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenant.Id,
                Action: AuditActions.TenantRegistered,
                ResourceType: ResourceTypes.Auth,
                ResourceId: tenant.Id.ToString(),
                ActorUserId: userId,
                ActorEmail: request.AdminEmail,
                DisplayName: request.TenantName), cancellationToken);
        }
        catch { /* audit must not block registration */ }

        return Result<AuthResultDto>.Success(
            AuthMapper.ToAuthResultDto(userId, request.AdminEmail, tenant.Id, tenant.Slug, roles, tokens));
    }

    private static (string Raw, string Hash) GenerateToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return (raw, hash);
    }

    private static string BuildConfirmUrl(string baseUrl, string userId, string token) =>
        $"{baseUrl.TrimEnd('/')}/auth/confirm-email?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";
}
