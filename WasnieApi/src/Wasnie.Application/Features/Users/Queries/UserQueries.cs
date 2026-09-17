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
