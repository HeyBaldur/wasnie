using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Assistant.Commands;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Assistant.DTOs;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Assistant;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Assistant.Handlers;

/// <summary>
/// One user turn in, one assistant turn out — the whole exchange collected before it is returned.
///
/// ★ WHY IT STILL EXISTS NOW THAT STREAMING DOES. The panel uses the streaming endpoint, because the
/// answer appearing as it is written is the product. This path is the same exchange without the
/// theatre, for any caller that cannot hold a stream open. It answers through the SAME provider and
/// the SAME prompt as the streaming handler, so the two cannot drift into giving different answers —
/// the reason a shared builder exists at all.
///
/// ATOMIC by the house pattern: the user turn, the reply and the conversation's UpdatedAt bump go in a
/// single <c>SaveChangesAsync</c>. A user message persisted without its reply would render as a
/// question the assistant ignored. If the model fails, NOTHING is written and the caller gets the
/// failure — unlike the streaming path, no fragment was shown, so there is nothing to keep.
/// </summary>
public sealed class PostMessageHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAssistantEntitlement entitlement,
    IChatCompletionProvider provider,
    IAssistantKnowledgeBase knowledge,
    AssistantSectionRouter router,
    IOptions<GroqOptions> options)
    : IRequestHandler<PostMessageCommand, Result<AssistantExchangeDto>>
{
    public async Task<Result<AssistantExchangeDto>> Handle(
        PostMessageCommand request, CancellationToken cancellationToken)
    {
        await entitlement.RequireAsync(cancellationToken);

        var conversation = await OwnedConversations.FindMineAsync(
            db, currentUser, request.ConversationId, cancellationToken);

        if (conversation is null)
            return Result<AssistantExchangeDto>.Failure(OwnedConversations.NotFound);

        var now = clock.UtcNowOffset;

        // Next position in the thread. Read from the stored turns rather than counted client-side,
        // because the client's idea of the thread can be stale.
        var lastSequence = await db.AssistantMessages
            .Where(m => m.ConversationId == conversation.Id)
            .Select(m => (int?)m.Sequence)
            .MaxAsync(cancellationToken) ?? -1;

        AssistantMessage userMessage;
        try
        {
            userMessage = AssistantMessage.Create(
                guid.NewGuid(), conversation.Id, tenantContext.TenantId,
                AssistantMessageRole.User, request.Content, lastSequence + 1, now);
        }
        catch (Wasnie.Domain.Exceptions.DomainException ex)
        {
            return Result<AssistantExchangeDto>.Failure(ex.Message);
        }

        var reply = await ComposeReplyAsync(conversation.Id, userMessage, cancellationToken);
        if (!reply.IsSuccess)
        {
            // Nothing was written at all — not even the user's turn. Unlike the streaming path there
            // was no fragment on screen, so refusing the whole exchange leaves no visible gap.
            return Result<AssistantExchangeDto>.Failure(reply.Error!);
        }

        AssistantMessage assistantMessage;
        try
        {
            assistantMessage = AssistantMessage.Create(
                guid.NewGuid(), conversation.Id, tenantContext.TenantId,
                AssistantMessageRole.Assistant, reply.Value!, lastSequence + 2, now);
        }
        catch (Wasnie.Domain.Exceptions.DomainException ex)
        {
            return Result<AssistantExchangeDto>.Failure(ex.Message);
        }

        // ★ The thread takes its name from the first thing said in it. Only while untitled: a name the
        // user chose outranks a derived one, and the second message never re-titles anything.
        conversation.TitleFromFirstMessage(ConversationTitle.FromMessage(userMessage.Content), now);

        conversation.Touch(now);
        db.AssistantMessages.AddRange(userMessage, assistantMessage);

        await db.SaveChangesAsync(cancellationToken);

        return Result<AssistantExchangeDto>.Success(new AssistantExchangeDto(
            AssistantMapper.ToDto(userMessage),
            AssistantMapper.ToDto(assistantMessage)));
    }

    /// <summary>
    /// The answer, collected. Same provider and same prompt as the streaming handler — the collecting
    /// is the only difference between the two paths.
    /// </summary>
    private async Task<Result<string>> ComposeReplyAsync(
        Guid conversationId, AssistantMessage userMessage, CancellationToken cancellationToken)
    {
        // No key configured → the stand-in, exactly as before a model existed.
        if (!provider.IsConfigured)
        {
            return Result<string>.Success(AssistantMessage.NotConnectedPlaceholder);
        }

        var history = await db.AssistantMessages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.Sequence)
            .ToListAsync(cancellationToken);

        history.Add(userMessage);

        // ── Step 1: which sections does this question need? ──────────────────
        // A small call against the table of contents only. Its result decides what step 2 carries.
        var sectionIds = await router.RouteAsync(userMessage.Content, cancellationToken);
        var routed = knowledge.TextFor(sectionIds);

        var prompt = AssistantPrompt.Build(
            history, options.Value.MaxHistoryMessages, routed, knowledge.IsAvailable);
        var answer = new StringBuilder();

        try
        {
            await foreach (var fragment in provider.StreamAsync(prompt, cancellationToken))
            {
                answer.Append(fragment);
            }
        }
        catch (ChatCompletionException ex)
        {
            // The translation KEY, never the provider's own words — see IChatCompletionProvider.
            return Result<string>.Failure(ex.ReasonKey);
        }

        var text = answer.ToString().Trim();

        // An empty completion is a failure in a success's clothes; storing it renders a blank bubble.
        return text.Length == 0
            ? Result<string>.Failure(ChatCompletionException.Unavailable)
            : Result<string>.Success(text.Length > AssistantMessage.MaxContentLength
                ? text[..AssistantMessage.MaxContentLength]
                : text);
    }
}
