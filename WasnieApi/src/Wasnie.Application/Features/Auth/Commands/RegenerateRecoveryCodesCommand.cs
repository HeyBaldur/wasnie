using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Commands;

public sealed record RegenerateRecoveryCodesCommand(string Password, string Code) : IRequest<Result<IEnumerable<string>>>;
