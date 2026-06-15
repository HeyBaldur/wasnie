using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Profile.Commands;

public sealed record ChangePasswordCommand(
    string CurrentPassword,
    string NewPassword,
    string ConfirmNewPassword) : IRequest<Result<bool>>;
