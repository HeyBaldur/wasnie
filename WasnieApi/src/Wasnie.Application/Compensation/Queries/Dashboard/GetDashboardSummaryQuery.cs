using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Dashboard;

/// <summary>
/// Returns all admin dashboard KPIs in a single call.
/// Period values: "this-month" (default) | "last-month" | "ytd" | "all-time"
/// Banda 1 (action items) is always period-independent.
/// Bandas 2 and 3 respond to the period parameter.
/// </summary>
public sealed record GetDashboardSummaryQuery(string Period = "this-month")
    : IRequest<Result<DashboardSummaryDto>>;
