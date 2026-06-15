using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Commands;

public sealed record RequestPasswordResetCommand(string Email) : IRequest<Result<bool>>;
