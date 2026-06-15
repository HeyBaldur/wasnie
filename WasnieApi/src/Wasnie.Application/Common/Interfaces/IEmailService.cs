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
}
