namespace Wasnie.Application.Common.Interfaces;

public interface IEmailService
{
    Task SendEmailConfirmationAsync(
        string to,
        string firstName,
        string confirmationUrl,
        string language,
        CancellationToken cancellationToken = default);

    Task SendPasswordResetAsync(
        string to,
        string firstName,
        string resetUrl,
        string language,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Warns the account holder that their account was locked after repeated failed sign-ins.
    /// </summary>
    Task SendAccountLockedAsync(
        string to,
        string firstName,
        string forgotPasswordUrl,
        int minutes,
        string language,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invites somebody who may have no Incentra account at all to join a tenant (KAN-32).
    /// </summary>
    Task SendInvitationAsync(
        string to,
        string inviterName,
        string companyName,
        string acceptUrl,
        int expiryDays,
        string language,
        CancellationToken cancellationToken = default);

    Task SendEmailChangeConfirmationAsync(
        string to,
        string firstName,
        string confirmationUrl,
        string language,
        CancellationToken cancellationToken = default);
}
