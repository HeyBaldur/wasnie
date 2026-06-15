using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Handlers;

public sealed class ConfirmEmailCommandHandler(
    IApplicationDbContext dbContext,
    IIdentityService identityService,
    IAuditService auditService,
    IClock clock)
    : IRequestHandler<ConfirmEmailCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        var tokenHash = ComputeHash(request.Token);
        var now = clock.UtcNowOffset;

        var record = await dbContext.EmailConfirmationTokens
            .FirstOrDefaultAsync(
                t => t.UserId == request.UserId && t.TokenHash == tokenHash,
                cancellationToken);

        if (record is null || !record.IsValid(now))
            return Result<bool>.Failure("Invalid or expired confirmation link. Request a new one.");

        record.MarkUsed(now);
        await dbContext.SaveChangesAsync(cancellationToken);

        var confirmed = await identityService.SetEmailConfirmedAsync(request.UserId);
        if (!confirmed)
            return Result<bool>.Failure("Failed to confirm email. Please try again.");

        var email = await identityService.FindEmailByUserIdAsync(request.UserId) ?? string.Empty;

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: Guid.Empty,
                Action: AuditActions.EmailConfirmed,
                ResourceType: ResourceTypes.Auth,
                ResourceId: request.UserId,
                ActorUserId: request.UserId,
                ActorEmail: email,
                DisplayName: email), cancellationToken);
        }
        catch { /* audit must not block confirmation */ }

        return Result<bool>.Success(true);
    }

    private static string ComputeHash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
