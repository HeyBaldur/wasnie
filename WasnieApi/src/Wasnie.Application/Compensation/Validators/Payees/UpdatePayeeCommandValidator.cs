using FluentValidation;
using Wasnie.Application.Compensation.Commands.Payees;

namespace Wasnie.Application.Compensation.Validators.Payees;

public sealed class UpdatePayeeCommandValidator : AbstractValidator<UpdatePayeeCommand>
{
    public UpdatePayeeCommandValidator()
    {
        RuleFor(x => x.PayeeId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EmployeeCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.HireDate)
            .Must(d => d >= new DateOnly(1950, 1, 1))
            .WithMessage("HireDate must be on or after 1950-01-01.")
            .Must(d => d <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("HireDate cannot be in the future.");
        RuleFor(x => x.Role).MaximumLength(100).When(x => x.Role is not null);
    }
}
