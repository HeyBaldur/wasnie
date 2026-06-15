using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Commands;

public sealed record ResetPasswordCommand(string UserId, string Token, string NewPassword) : IRequest<Result<bool>>;
