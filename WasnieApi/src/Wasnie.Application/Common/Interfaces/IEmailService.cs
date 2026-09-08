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

    Task SendEmailChangeConfirmationAsync(
        string to,
        string firstName,
        string confirmationUrl,
        string language,
        CancellationToken cancellationToken = default);
}
