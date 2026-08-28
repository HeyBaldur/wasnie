using FluentValidation;
using Wasnie.Application.Assistant.Commands;

namespace Wasnie.Application.Assistant.Validators;

/// <summary>
/// What a pin request has to carry.
///
/// ★★ THE CAP IS NOT HERE, AND THAT IS A DECISION RATHER THAN AN OMISSION. "How many do I already have
/// pinned" depends on the database AND on who is asking, and a FluentValidation failure throws a
/// ValidationException whose message is a SENTENCE — while this list is read in English, Spanish and
/// Polish and needs a translation KEY. A validator that queries the database on behalf of the principal
/// is a handler wearing a different name. So the shape is checked here and the limit is enforced in
/// PinConversationHandler, which returns AssistantPins.LimitReachedKey.
/// </summary>
public sealed class PinConversationCommandValidator : AbstractValidator<PinConversationCommand>
{
    public PinConversationCommandValidator()
    {
        RuleFor(c => c.ConversationId).NotEmpty();
    }
}

public sealed class UnpinConversationCommandValidator : AbstractValidator<UnpinConversationCommand>
{
    public UnpinConversationCommandValidator()
    {
        RuleFor(c => c.ConversationId).NotEmpty();
    }
}
