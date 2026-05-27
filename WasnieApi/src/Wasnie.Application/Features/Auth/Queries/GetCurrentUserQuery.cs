using MediatR;
using Wasnie.Application.Features.Auth.DTOs;

namespace Wasnie.Application.Features.Auth.Queries;

public sealed record GetCurrentUserQuery : IRequest<CurrentUserDto>;
