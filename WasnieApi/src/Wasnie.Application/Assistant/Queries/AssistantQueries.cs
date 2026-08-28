using MediatR;
using Wasnie.Application.Assistant.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Assistant.Queries;

/// <summary>
/// One batch of my conversations, most recently active first. "My" is not a filter the caller chooses.
///
/// ★ THE SEARCH IS A PARAMETER OF THIS QUERY, NOT A SECOND ENDPOINT. A separate search route would be
/// a second implementation of the ordering, the cursor and the response shape, and the two would drift
/// — which shows up as a search result list that pages differently from the list beside it. One query,
/// one order, one cursor: searching narrows the set and changes nothing else.
/// </summary>
/// <param name="Cursor">
/// Opaque; echoed back from a previous response. Null starts at the top. See
/// <see cref="Common.ConversationCursor"/> for why it names a row rather than a position.
/// </param>
/// <param name="PageSize">Null takes the default. Range is enforced by the validator, not clamped here.</param>
/// <param name="Search">
/// Matched against the TITLE only. Below <see cref="AssistantPaging.MinSearchLength"/> characters it is
/// ignored and the ordinary list comes back — see the validator's sibling comment.
/// </param>
public sealed record ListConversationsQuery(
    string? Cursor = null,
    int? PageSize = null,
    string? Search = null) : IRequest<Result<AssistantConversationPageDto>>;

public sealed record GetConversationQuery(Guid ConversationId) : IRequest<Result<AssistantConversationDto>>;

/// <summary>
/// Whether the current user may use the assistant. Deliberately answerable by ANY authenticated user —
/// it is the question "do I get the button?", and a user without a seat must get a clean `false`
/// rather than a 403 they cannot act on.
/// </summary>
public sealed record GetAssistantEntitlementQuery : IRequest<Result<AssistantEntitlementDto>>;
