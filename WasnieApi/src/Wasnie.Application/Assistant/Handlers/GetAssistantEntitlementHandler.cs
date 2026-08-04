using MediatR;
using Wasnie.Application.Assistant.DTOs;
using Wasnie.Application.Assistant.Queries;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Assistant.Handlers;

/// <summary>
/// The only assistant endpoint that does NOT require the entitlement — it reports it.
///
/// A user without a seat asking "do I get the assistant?" must receive a calm `false`, not a 403. The
/// 403 is for someone who tried to USE it; this is the question the UI asks on load to decide whether
/// to render the entry point at all (RBAC = hide, not disable).
/// </summary>
public sealed class GetAssistantEntitlementHandler(IAssistantEntitlement entitlement)
    : IRequestHandler<GetAssistantEntitlementQuery, Result<AssistantEntitlementDto>>
{
    public async Task<Result<AssistantEntitlementDto>> Handle(
        GetAssistantEntitlementQuery request, CancellationToken cancellationToken) =>
        Result<AssistantEntitlementDto>.Success(
            new AssistantEntitlementDto(await entitlement.IsEnabledAsync(cancellationToken)));
}
