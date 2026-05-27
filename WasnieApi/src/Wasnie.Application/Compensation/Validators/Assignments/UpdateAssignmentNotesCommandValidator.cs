using FluentValidation;
using Wasnie.Application.Compensation.Commands.Assignments;

namespace Wasnie.Application.Compensation.Validators.Assignments;

public sealed class UpdateAssignmentNotesCommandValidator : AbstractValidator<UpdateAssignmentNotesCommand>
{
    public UpdateAssignmentNotesCommandValidator()
    {
        RuleFor(x => x.AssignmentId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
    }
}
