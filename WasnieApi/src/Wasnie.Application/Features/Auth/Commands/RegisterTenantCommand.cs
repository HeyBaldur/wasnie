using MediatR;
using Wasnie.Domain.Common.Results;
using Wasnie.Application.Features.Auth.DTOs;

namespace Wasnie.Application.Features.Auth.Commands;

public sealed record RegisterTenantCommand(
    string TenantName,
    string TenantSlug,
    string AdminEmail,
    string AdminPassword,
    string AdminFirstName,
    string AdminLastName) : IRequest<Result<AuthResultDto>>;
