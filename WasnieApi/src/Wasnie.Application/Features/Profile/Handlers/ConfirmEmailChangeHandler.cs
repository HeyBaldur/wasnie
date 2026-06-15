using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Profile.Commands;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Profile.Handlers;

public sealed class ConfirmEmailChangeHandler(
    IIdentityService identityService,
    IApplicationDbContext dbContext,
    IAuditService auditService,
    IClock clock)
    : IRequestHandler<ConfirmEmailChangeCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ConfirmEmailChangeCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset;
        var tokenHash = ComputeHash(request.Token);

        var record = await dbContext.EmailChangeTokens
            .FirstOrDefaultAsync(
                t => t.UserId == request.UserId && t.TokenHash == tokenHash,
                cancellationToken);

        if (record is null || !record.IsValid(now))
            return Result<bool>.Failure("This email change link is invalid or has expired.");

        var oldEmail = await identityService.FindEmailByUserIdAsync(request.UserId) ?? string.Empty;
        var newEmail = record.NewEmail;

        // Final uniqueness check at confirmation time (race-condition guard).
        var existingId = await identityService.FindUserIdByEmailAsync(newEmail);
        if (existingId is not null && existingId != request.UserId)
            return Result<bool>.Failure("That email address is already in use.");

        var changed = await identityService.ChangeEmailAsync(request.UserId, newEmail);
        if (!changed)
            return Result<bool>.Failure("Failed to update email. Please request a new change.");

        record.MarkUsed(now);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            // TenantId is Guid.Empty here because we only have userId — not in tenant context for anonymous confirm.
            await auditService.LogAsync(new AuditEntry(
                TenantId: Guid.Empty,
                Action: AuditActions.ProfileEmailChangeConfirmed,
                ResourceType: ResourceTypes.Auth,
                ResourceId: request.UserId,
                ActorUserId: request.UserId,
                ActorEmail: newEmail,
                DisplayName: newEmail,
                Before: new { Email = oldEmail },
                After: new { Email = newEmail }),
                cancellationToken);
        }
        catch { /* audit must not block */ }

        return Result<bool>.Success(true);
    }

    private static string ComputeHash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
