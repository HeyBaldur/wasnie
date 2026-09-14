using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Settings;

namespace Wasnie.Application.Features.UiPreferences;

/// <summary>All of the signed-in user's UI preferences, as key → value (KAN-78).</summary>
public sealed record GetUiPreferencesQuery : IRequest<Result<IReadOnlyDictionary<string, string>>>;

/// <summary>Creates or replaces one of the signed-in user's UI preferences.</summary>
/// <remarks>Deliberately NOT an auditable command: "dismissed a tooltip" is not a business event.</remarks>
public sealed record SetUiPreferenceCommand(string Key, string Value) : IRequest<Result<bool>>;

public sealed class SetUiPreferenceCommandValidator : AbstractValidator<SetUiPreferenceCommand>
{
    public SetUiPreferenceCommandValidator()
    {
        RuleFor(x => x.Key)
            .Must(UserUiPreference.IsValidKey)
            .WithMessage($"A preference key is 1-{UserUiPreference.MaxKeyLength} lowercase letters, digits, dots or dashes.");
        RuleFor(x => x.Value).NotNull().MaximumLength(UserUiPreference.MaxValueLength);
    }
}

public sealed class GetUiPreferencesHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser)
    : IRequestHandler<GetUiPreferencesQuery, Result<IReadOnlyDictionary<string, string>>>
{
    public async Task<Result<IReadOnlyDictionary<string, string>>> Handle(
        GetUiPreferencesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId) || !tenantContext.IsResolved)
            return Result<IReadOnlyDictionary<string, string>>.Failure("No signed-in user.");

        var rows = await db.UserUiPreferences
            .Where(p => p.TenantId == tenantContext.TenantId && p.UserId == userId)
            .Select(p => new { p.Key, p.Value })
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyDictionary<string, string>>.Success(rows.ToDictionary(r => r.Key, r => r.Value));
    }
}

public sealed class SetUiPreferenceHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock)
    : IRequestHandler<SetUiPreferenceCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SetUiPreferenceCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId) || !tenantContext.IsResolved)
            return Result<bool>.Failure("No signed-in user.");

        var existing = await db.UserUiPreferences.FirstOrDefaultAsync(
            p => p.TenantId == tenantContext.TenantId && p.UserId == userId && p.Key == request.Key,
            cancellationToken);

        if (existing is null)
            db.UserUiPreferences.Add(UserUiPreference.Create(
                Guid.NewGuid(), tenantContext.TenantId, userId, request.Key, request.Value, clock.UtcNowOffset));
        else
            existing.SetValue(request.Value, clock.UtcNowOffset);

        await db.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
