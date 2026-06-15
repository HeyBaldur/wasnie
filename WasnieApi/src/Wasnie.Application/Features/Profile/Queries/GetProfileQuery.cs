using MediatR;
using Wasnie.Application.Features.Profile.DTOs;

namespace Wasnie.Application.Features.Profile.Queries;

public sealed record GetProfileQuery : IRequest<ProfileDto>;
