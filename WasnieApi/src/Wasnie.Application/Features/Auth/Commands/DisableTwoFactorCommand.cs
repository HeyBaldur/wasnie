using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Commands;

public sealed record DisableTwoFactorCommand(string Password, string Code) : IRequest<Result<Unit>>;
