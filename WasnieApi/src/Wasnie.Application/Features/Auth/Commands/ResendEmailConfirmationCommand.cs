using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Commands;

public sealed record ResendEmailConfirmationCommand(string Email) : IRequest<Result<bool>>;
