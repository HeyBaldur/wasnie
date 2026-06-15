using MediatR;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Auth.Queries;

public sealed record GetTwoFactorStatusQuery : IRequest<Result<TwoFactorStatusDto>>;
