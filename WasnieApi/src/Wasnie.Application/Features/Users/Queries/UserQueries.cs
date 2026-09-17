using MediatR;
using Wasnie.Application.Features.Users.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Users.Queries;

/// <summary>
/// The users screen: people, outstanding invitations and the seat position, in one response.
/// Requires Users.Read.
/// </summary>
public sealed record ListTenantUsersQuery : IRequest<Result<TenantUsersResponse>>;

/// <summary>
/// What the public accept page shows before anybody signs in. PUBLIC — the token is the
/// authorisation, and the answer carries no identifiers.
/// </summary>
public sealed record GetInvitationByTokenQuery(string Token) : IRequest<Result<InvitationPreviewDto>>;

/// <summary>
/// The payees in this workspace that no login owns yet — what the invite form offers (KAN-92).
///
/// ONLY THE UNLINKED ONES. A payee that already belongs to somebody is not a choice: picking it
/// would mean moving who sees that person's pay, which is a deliberate act and not a side effect of
/// inviting. Requires Users.Manage, because it is part of granting access.
/// </summary>
public sealed record ListUnlinkedPayeesQuery(string? EmailHint = null)
    : IRequest<Result<UnlinkedPayeesResponse>>;
