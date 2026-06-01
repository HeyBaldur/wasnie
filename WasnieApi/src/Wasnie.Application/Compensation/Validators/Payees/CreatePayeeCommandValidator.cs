using FluentValidation;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Payees;

namespace Wasnie.Application.Compensation.Validators.Payees;

public sealed class CreatePayeeCommandValidator : AbstractValidator<CreatePayeeCommand>
{
    public CreatePayeeCommandValidator(IFieldRequirementService fieldRequirements)
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EmployeeCode).NotEmpty().MaximumLength(50);

        // Required when tenant setting is Required; format always enforced when present
        RuleFor(x => x.Email)
            .MustAsync(async (email, ct) =>
                !string.IsNullOrWhiteSpace(email) ||
                !await fieldRequirements.IsRequiredAsync("Payee", "Email", ct))
            .WithMessage("Email is required.");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Email address is not valid.")
            .MaximumLength(255)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        // Required when tenant setting is Required; range always enforced when present
        RuleFor(x => x.HireDate)
            .MustAsync(async (hireDate, ct) =>
                hireDate.HasValue ||
                !await fieldRequirements.IsRequiredAsync("Payee", "HireDate", ct))
            .WithMessage("Hire date is required.");

        RuleFor(x => x.HireDate)
            .Must(d => d!.Value >= new DateOnly(1950, 1, 1))
            .WithMessage("HireDate must be on or after 1950-01-01.")
            .Must(d => d!.Value <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("HireDate cannot be in the future.")
            .When(x => x.HireDate.HasValue);

        RuleFor(x => x.Role).MaximumLength(100).When(x => x.Role is not null);
    }
}
