using Wasnie.Application.Common.DTOs;

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

    /// <summary>
    /// Sends an administrator the Organization identifier(s) of the workspaces they administer (KAN-93).
    ///
    /// ★★ IT CARRIES NO LINK THAT DOES ANYTHING. Unlike every other message in this interface there is
    /// no token, no one-off URL and nothing to click that changes state: the identifier IS the answer,
    /// and <paramref name="loginUrl"/> is the ordinary sign-in page the reader could type themselves.
    /// A security email that arrives with a freshly minted action link is the exact shape phishing
    /// imitates — the same reasoning <see cref="SendAccountLockedAsync"/> already spells out.
    ///
    /// ★ A LIST, NOT A STRING. Somebody can administer more than one workspace, and the plural case is
    /// the one where getting it wrong hurts: sending the first of two identifiers looks like a working
    /// feature and leaves the reader locked out of the other company.
    /// </summary>
    Task SendOrganizationIdentifierAsync(
        string to,
        string firstName,
        IReadOnlyList<OrganizationIdentifier> organizations,
        string loginUrl,
        string language,
        CancellationToken cancellationToken = default);

    Task SendEmailChangeConfirmationAsync(
        string to,
        string firstName,
        string confirmationUrl,
        string language,
        CancellationToken cancellationToken = default);
}
