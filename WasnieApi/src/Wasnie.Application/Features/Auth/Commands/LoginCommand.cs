using MediatR;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Commands;

/// <summary>
/// KAN-91. <paramref name="OrganizationId"/> is the workspace's <c>Slug</c> — what an administrator
/// hands their team, in the shape of <c>wasnie-ldta-polska</c>.
///
/// ★★ IT IS OPTIONAL, AND THAT IS WHAT KEEPS TODAY'S USERS UNTOUCHED. Somebody who belongs to one
/// workspace never has to know it exists; the field only becomes necessary when an address is in more
/// than one, which is the whole point of this change. Making it required would have taught every
/// existing customer a new step to solve a problem they do not have.
/// </summary>
public sealed record LoginCommand(
    string Email,
    string Password,
    string? OrganizationId = null) : IRequest<Result<AuthResultDto>>;
