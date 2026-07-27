using FluentValidation;
using Wasnie.Application.Compensation.Commands.Enrichment;
using Wasnie.Domain.Compensation.Enrichment;

namespace Wasnie.Application.Compensation.Validators.Enrichment;

public sealed class CreateCategoryMappingCommandValidator : AbstractValidator<CreateCategoryMappingCommand>
{
    public CreateCategoryMappingCommandValidator()
    {
        RuleFor(x => x.InputField)
            .Must(CategoryMapping.Fields.IsValid)
            .WithMessage(_ => $"Input field must be one of: {string.Join(", ", CategoryMapping.Fields.All)}.");

        RuleFor(x => x.InputValue)
            .NotEmpty().WithMessage("Input value is required.")
            .MaximumLength(CategoryMapping.MaxInputValueLength);

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Category is required.")
            .MaximumLength(CategoryMapping.MaxCategoryLength);
    }
}
