using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Dashboard;

/// <summary>
/// Returns all admin dashboard KPIs in a single call, for a free [From, To] date range.
///
/// ★ THE PRESETS ARE GONE FROM THIS SCREEN, NOT FROM THE PRODUCT. "this-month" / "ytd" and the rest
/// still drive the payouts list, pay runs, the payee detail, the ledger summary, quotas and
/// assignments through <c>PeriodHelper</c>. Only the dashboard takes a range now.
///
/// Both bounds are OPTIONAL here and defaulted by the handler to the whole current month, so a caller
/// that sends neither still gets the screen's opening view rather than an unbounded scan.
///
/// The range governs the period band, the three commission cards and the trend. The ACTION band is
/// deliberately left outside it: those are items of work, not a report, and a draft pay run raised in
/// September still has to be approved while the reader is looking at February.
/// </summary>
public sealed record GetDashboardSummaryQuery(DateOnly? From = null, DateOnly? To = null)
    : IRequest<Result<DashboardSummaryDto>>;
