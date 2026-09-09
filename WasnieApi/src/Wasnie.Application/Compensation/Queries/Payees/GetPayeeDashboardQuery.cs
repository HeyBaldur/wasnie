using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Payees;

/// <summary>
/// The payee Overview, for a free [From, To] range — the same shape the dashboard took in KAN-62.
/// Both bounds optional; the handler defaults them to the whole current month.
/// </summary>
public sealed record GetPayeeDashboardQuery(Guid PayeeId, DateOnly? From = null, DateOnly? To = null)
    : IRequest<Result<PayeeDashboardDto>>;
