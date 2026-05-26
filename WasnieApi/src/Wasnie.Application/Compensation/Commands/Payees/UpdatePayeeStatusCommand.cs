using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Payees;

public sealed record MarkPayeeAsActiveCommand(Guid PayeeId) : IRequest<Result>;
public sealed record MarkPayeeAsOnLeaveCommand(Guid PayeeId) : IRequest<Result>;
public sealed record MarkPayeeAsTerminatedCommand(Guid PayeeId, DateOnly TerminationDate) : IRequest<Result>;
