using System.Text;
using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.Application.Features.Auth.Queries;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Handlers;

public sealed class GetTwoFactorSetupHandler(
    ICurrentUserService currentUser,
    IIdentityService identityService)
    : IRequestHandler<GetTwoFactorSetupQuery, Result<TwoFactorSetupDto>>
{
    public async Task<Result<TwoFactorSetupDto>> Handle(
        GetTwoFactorSetupQuery request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
            return Result<TwoFactorSetupDto>.Failure("User not found.");

        var rawSecret = await identityService.GetOrCreateTotpSecretAsync(userId);
        if (rawSecret is null)
            return Result<TwoFactorSetupDto>.Failure("Could not generate 2FA secret.");

        var email = currentUser.Email ?? userId;
        var otpauthUri = BuildOtpAuthUri("Incentra", email, rawSecret);

        return Result<TwoFactorSetupDto>.Success(new TwoFactorSetupDto(rawSecret, otpauthUri));
    }

    private static string BuildOtpAuthUri(string issuer, string accountName, string secret)
    {
        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedAccount = Uri.EscapeDataString(accountName);
        return $"otpauth://totp/{encodedIssuer}:{encodedAccount}?secret={secret}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30";
    }
}
