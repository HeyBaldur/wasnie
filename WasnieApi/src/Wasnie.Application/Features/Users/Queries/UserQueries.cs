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
/// The payees in this workspace that no login owns yet — what the pickers offer (KAN-92, KAN-93).
///
/// ONLY THE UNLINKED ONES. A payee that already belongs to somebody is not a choice: picking it
/// would mean moving who sees that person's pay, which is a deliberate act and not a side effect of
/// inviting. Requires Users.Manage, because it is part of granting access.
///
/// ★★ IT IS A TYPEAHEAD, NOT A ROSTER (KAN-93). The first cut returned every unlinked payee in one
/// unpaginated list and both pickers rendered it whole. That is a usable screen at ten payees and an
/// unusable one at a thousand: a dropdown nobody can scroll to the right name in, paid for by shipping
/// the entire staff list — names, codes and EMAIL ADDRESSES — on every modal open. <paramref
/// name="Search"/> moves the filtering to the database and the handler caps what comes back, which is
/// the same shape every other payee picker in this product already uses.
/// </summary>
/// <param name="EmailHint">
/// An address to look for a match on. Answered SEPARATELY from the search results and never affected
/// by <paramref name="Search"/> — see the handler.
/// </param>
public sealed record ListUnlinkedPayeesQuery(string? EmailHint = null, string? Search = null)
    : IRequest<Result<UnlinkedPayeesResponse>>;
