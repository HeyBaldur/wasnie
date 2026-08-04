using MediatR;
using Wasnie.Application.Assistant.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Assistant.Queries;

/// <summary>My conversations, most recently active first. "My" is not a filter the caller chooses.</summary>
public sealed record ListConversationsQuery : IRequest<Result<IReadOnlyList<AssistantConversationSummaryDto>>>;

public sealed record GetConversationQuery(Guid ConversationId) : IRequest<Result<AssistantConversationDto>>;

/// <summary>
/// Whether the current user may use the assistant. Deliberately answerable by ANY authenticated user —
/// it is the question "do I get the button?", and a user without a seat must get a clean `false`
/// rather than a 403 they cannot act on.
/// </summary>
public sealed record GetAssistantEntitlementQuery : IRequest<Result<AssistantEntitlementDto>>;
