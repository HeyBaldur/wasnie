using FluentValidation;
using Wasnie.Application.Assistant.Commands;
using Wasnie.Domain.Assistant;

namespace Wasnie.Application.Assistant.Validators;

/// <summary>
/// Request SHAPE only. Whether a message or a title may exist is decided by the domain
/// (<see cref="AssistantMessage.Create"/>, <see cref="AssistantConversation"/>), and the bounds here
/// are the same numbers the entities enforce — stated twice so the client gets a 400 with a field
/// name instead of a generic failure, never so the two can disagree.
/// </summary>
public sealed class StartConversationCommandValidator : AbstractValidator<StartConversationCommand>
{
    public StartConversationCommandValidator()
    {
        RuleFor(x => x.Title)
            .MaximumLength(AssistantConversation.MaxTitleLength)
            .When(x => x.Title is not null);
    }
}

public sealed class PostMessageCommandValidator : AbstractValidator<PostMessageCommand>
{
    public PostMessageCommandValidator()
    {
        RuleFor(x => x.ConversationId).NotEmpty();
        RuleFor(x => x.Content).NotEmpty().MaximumLength(AssistantMessage.MaxContentLength);
    }
}

public sealed class RenameConversationCommandValidator : AbstractValidator<RenameConversationCommand>
{
    public RenameConversationCommandValidator()
    {
        RuleFor(x => x.ConversationId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(AssistantConversation.MaxTitleLength);
    }
}

public sealed class DeleteConversationCommandValidator : AbstractValidator<DeleteConversationCommand>
{
    public DeleteConversationCommandValidator()
    {
        RuleFor(x => x.ConversationId).NotEmpty();
    }
}
