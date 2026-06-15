using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.Application.Features.Auth.Queries;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Handlers;

public sealed class GetTwoFactorStatusHandler(
    ICurrentUserService currentUser,
    IIdentityService identityService)
    : IRequestHandler<GetTwoFactorStatusQuery, Result<TwoFactorStatusDto>>
{
    public async Task<Result<TwoFactorStatusDto>> Handle(
        GetTwoFactorStatusQuery request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
            return Result<TwoFactorStatusDto>.Failure("User not found.");

        var isEnabled = await identityService.IsTwoFactorEnabledAsync(userId);
        var recoveryCodeCount = isEnabled
            ? await identityService.CountRecoveryCodesAsync(userId)
            : 0;

        return Result<TwoFactorStatusDto>.Success(new TwoFactorStatusDto(isEnabled, recoveryCodeCount));
    }
}
