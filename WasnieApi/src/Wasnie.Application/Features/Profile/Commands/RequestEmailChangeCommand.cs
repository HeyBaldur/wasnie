using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Profile.Commands;

public sealed record RequestEmailChangeCommand(string NewEmail) : IRequest<Result<bool>>;
