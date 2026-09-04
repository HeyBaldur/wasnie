using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Sidebar;

/// <summary>
/// The counts the sidebar puts beside its links, in one call.
///
/// ★ ONE ENDPOINT, NOT ONE PER BADGE. Three requests on every page load, each paying for its own
/// authorisation and tenant resolution, to draw three small numbers.
/// </summary>
public sealed record GetSidebarBadgesQuery : IRequest<Result<SidebarBadgesDto>>;

/// <summary>
/// What the sidebar may show this user.
///
/// ★★ NULL MEANS "NOT YOURS TO SEE", AND IT IS NOT THE SAME AS ZERO. A user without
/// <c>Reports.ViewAll</c> must see no reconciliation badge at all — a 0 would tell them the queue is
/// empty, which is a statement about the tenant's money they were not cleared to receive. The front
/// draws nothing for null and draws the number for 0.
/// </summary>
/// <param name="Reconciliation">Open rows in the Reconciliation Centre, or null when not permitted.</param>
/// <param name="TerminatedAccounts">Open orphan-account rows, or null when not permitted.</param>
/// <param name="FinancialsTotal">
/// The sum of the badges above the Financials group holds — counting only what the user may see, so
/// the group total can never leak a figure its own children are hiding.
/// </param>
public sealed record SidebarBadgesDto(
    int? Reconciliation,
    int? TerminatedAccounts,
    int FinancialsTotal);
