using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Profile.Commands;

public sealed record UpdateProfileNameCommand(
    string FirstName,
    string LastName) : IRequest<Result<bool>>;
