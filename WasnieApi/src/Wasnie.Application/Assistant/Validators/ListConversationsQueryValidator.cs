using FluentValidation;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Assistant.Queries;

namespace Wasnie.Application.Assistant.Validators;

/// <summary>
/// What a caller may ask the conversation list for.
///
/// ★ ONLY THE PAGE SIZE IS VALIDATED, AND THE OTHER TWO ARE DELIBERATE OMISSIONS.
///
/// The CURSOR is not checked here because an unreadable one is not an error — it is the first page.
/// Cursors travel through URLs and bookmarks and go stale; a 400 would turn a harmless staleness into
/// a broken screen. <see cref="ConversationCursor.Decode"/> owns that decision, and it is safe to make
/// there because the scoping does not depend on the cursor.
///
/// The SEARCH term is not checked because a term shorter than the minimum means "still typing", not
/// "invalid". Rejecting it would flash an error between the first and second keystroke of every search
/// anybody ever runs. The handler ignores it and returns the ordinary list.
///
/// The page size is different: it can only come from a caller that decided a number, so a number
/// outside the range is a decision that was wrong, and saying so is more useful than silently
/// substituting a different one.
/// </summary>
public sealed class ListConversationsQueryValidator : AbstractValidator<ListConversationsQuery>
{
    public ListConversationsQueryValidator()
    {
        // Null is "use the default" and must not be validated as a number — hence the guard rather
        // than a rule on the nullable value.
        When(q => q.PageSize.HasValue, () =>
        {
            RuleFor(q => q.PageSize!.Value)
                .InclusiveBetween(1, AssistantPaging.MaxPageSize)
                .WithMessage(
                    $"pageSize must be between 1 and {AssistantPaging.MaxPageSize}.");
        });
    }
}
