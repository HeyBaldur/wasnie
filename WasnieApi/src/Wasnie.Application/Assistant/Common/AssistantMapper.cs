using Wasnie.Application.Assistant.DTOs;
using Wasnie.Domain.Assistant;

namespace Wasnie.Application.Assistant.Common;

public static class AssistantMapper
{
    public static AssistantMessageDto ToDto(AssistantMessage message) =>
        new(message.Id,
            message.Role.ToString(),
            message.Content,
            message.Payload,
            message.Sequence,
            message.CreatedAt);

    public static AssistantConversationDto ToDto(
        AssistantConversation conversation, IReadOnlyList<AssistantMessage> messages) =>
        new(conversation.Id,
            conversation.Title,
            conversation.CreatedAt,
            conversation.UpdatedAt,
            messages.Select(ToDto).ToList());

    public static AssistantConversationSummaryDto ToSummary(
        AssistantConversation conversation, int messageCount) =>
        new(conversation.Id,
            conversation.Title,
            conversation.CreatedAt,
            conversation.UpdatedAt,
            messageCount);
}
