namespace Wasnie.Application.Features.Profile.DTOs;

/// <param name="IdentityManagedByAdministrator">
/// Whether an administrator maintains this person's name and address rather than the person
/// themselves — see <see cref="AdministeredIdentity"/> for why the payee link is the discriminator.
///
/// ★★ THE SCREEN READS IT TO HIDE THE TWO CARDS, AND THE SERVER REFUSES ANYWAY. Hiding alone would
/// leave PUT /api/profile/name and the email-change request open to anybody with a session — the
/// screen saying no while the endpoint says yes is the same shape as hiding a menu entry and leaving
/// the URL reachable, which this codebase has already been bitten by once.
///
/// ★ IT IS AN ANSWER, NOT AN INPUT. Nothing the caller sends decides it.
/// </param>
public sealed record ProfileDto(
    string FirstName,
    string LastName,
    string Email,
    bool HasPendingEmailChange,
    string CompanyName,
    string OrganizationSlug,
    bool IdentityManagedByAdministrator = false);
