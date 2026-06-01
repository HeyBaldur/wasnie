using FluentValidation;
using Wasnie.Application.Common.Constants;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Payees;
using Wasnie.Domain.Compensation.Payees;

namespace Wasnie.Application.Compensation.Validators.Payees;

public sealed class UpdatePayeeCommandValidator : AbstractValidator<UpdatePayeeCommand>
{
    public UpdatePayeeCommandValidator(IFieldRequirementService fieldRequirements)
    {
        RuleFor(x => x.PayeeId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EmployeeCode).NotEmpty().MaximumLength(50);

        // Required when tenant setting is Required; format always enforced when present
        RuleFor(x => x.Email)
            .MustAsync(async (email, ct) =>
                !string.IsNullOrWhiteSpace(email) ||
                !await fieldRequirements.IsRequiredAsync(PayeeFieldNames.Entity, PayeeFieldNames.Email, ct))
            .WithMessage("Email is required.");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Email address is not valid.")
            .MaximumLength(255)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        // Required when tenant setting is Required; range always enforced when present
        RuleFor(x => x.HireDate)
            .MustAsync(async (hireDate, ct) =>
                hireDate.HasValue ||
                !await fieldRequirements.IsRequiredAsync(PayeeFieldNames.Entity, PayeeFieldNames.HireDate, ct))
            .WithMessage("Hire date is required.");

        RuleFor(x => x.HireDate)
            .Must(d => d!.Value >= new DateOnly(1950, 1, 1))
            .WithMessage("HireDate must be on or after 1950-01-01.")
            .Must(d => d!.Value <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("HireDate cannot be in the future.")
            .When(x => x.HireDate.HasValue);

        RuleFor(x => x.Role)
            .MustAsync(async (role, ct) =>
                !string.IsNullOrWhiteSpace(role) ||
                !await fieldRequirements.IsRequiredAsync(PayeeFieldNames.Entity, PayeeFieldNames.Role, ct))
            .WithMessage("Role is required.");

        RuleFor(x => x.Role).MaximumLength(100).When(x => x.Role is not null);

        RuleFor(x => x.ManagerId)
            .MustAsync(async (managerId, ct) =>
                managerId.HasValue ||
                !await fieldRequirements.IsRequiredAsync(PayeeFieldNames.Entity, PayeeFieldNames.ManagerId, ct))
            .WithMessage("Manager is required.");

        RuleFor(x => x.EmploymentType)
            .MustAsync(async (employmentType, ct) =>
                !string.IsNullOrWhiteSpace(employmentType) ||
                !await fieldRequirements.IsRequiredAsync(PayeeFieldNames.Entity, PayeeFieldNames.EmploymentType, ct))
            .WithMessage("Employment type is required.");

        RuleFor(x => x.EmploymentType)
            .Must(et => Enum.TryParse<EmploymentType>(et, ignoreCase: true, out _))
            .WithMessage(x => $"Invalid employment type '{x.EmploymentType}'. Expected one of: FullTime, PartTime, Temporary, Contractor.")
            .When(x => !string.IsNullOrWhiteSpace(x.EmploymentType));

        RuleFor(x => x.Location)
            .MustAsync(async (location, ct) =>
                !string.IsNullOrWhiteSpace(location) ||
                !await fieldRequirements.IsRequiredAsync(PayeeFieldNames.Entity, PayeeFieldNames.Location, ct))
            .WithMessage("Location is required.");

        RuleFor(x => x.Location).MaximumLength(200).When(x => x.Location is not null);
    }
}
