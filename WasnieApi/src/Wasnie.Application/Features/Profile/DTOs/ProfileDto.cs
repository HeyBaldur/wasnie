namespace Wasnie.Application.Features.Profile.DTOs;

public sealed record ProfileDto(
    string FirstName,
    string LastName,
    string Email,
    bool HasPendingEmailChange,
    string CompanyName,
    string OrganizationSlug);
