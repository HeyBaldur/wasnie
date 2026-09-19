using FluentValidation;
using Wasnie.Application.Features.Users.Commands;
using Wasnie.Domain.Authorization;

namespace Wasnie.Application.Features.Users.Validators;

/// <summary>
/// Shape checks only (KAN-32).
///
/// ★ THE INTERESTING REFUSALS ARE NOT HERE, AND THAT IS ON PURPOSE. "Already a member", "already
/// invited", "no seats" and "last admin" all need the database and all have to reach the reader as
/// CODES so three languages can render them (§C1). FluentValidation produces English sentences, so it
/// gets the questions that can be answered from the request alone and nothing else.
///
/// ★ IsKnown, NOT IsAssignable. A hidden role (CompManager, Manager) is refused by the handler with
/// INVITATION_ROLE_NOT_ASSIGNABLE, a code the screen translates; refusing it here would answer in an
/// English sentence instead.
/// </summary>
public sealed class InviteUserCommandValidator : AbstractValidator<InviteUserCommand>
{
    public InviteUserCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(256).WithMessage("Email must be 256 characters or fewer.")
            .EmailAddress().WithMessage("Email must be a valid address.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.")
            .Must(Roles.IsKnown).WithMessage("Role is not one this product has.");
    }
}

public sealed class ChangeUserRoleCommandValidator : AbstractValidator<ChangeUserRoleCommand>
{
    public ChangeUserRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.")
            .Must(Roles.IsKnown).WithMessage("Role is not one this product has.");
    }
}

/// <summary>
/// The accept form. The password rule is the same one the rest of the product enforces — a person
/// invited into a tenant is not held to a weaker standard than one who signed up.
/// </summary>
public sealed class AcceptInvitationCommandValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().WithMessage("Token is required.");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name must be 100 characters or fewer.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name must be 100 characters or fewer.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(10).WithMessage("Password must be at least 10 characters.")
            .Matches("[A-Z]").WithMessage("Password must include an uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must include a digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must include a special character.");
    }
}
