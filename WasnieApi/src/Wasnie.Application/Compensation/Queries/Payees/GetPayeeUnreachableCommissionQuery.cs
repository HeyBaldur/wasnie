using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Payees;

/// <summary>
/// Commission this payee is owed that NO pay run can reach, and what would have to change for it to
/// become payable.
///
/// ★★ NOT FILTERED BY DATE, ON PURPOSE. Every other view of this money is scoped to a range, and that
/// is exactly how it stayed invisible: the debt is spread across periods, so any window a reader picks
/// shows a slice and hides the rest. This is a "what is stuck" screen, not a report of a month.
///
/// ★ IT ANSWERS "WHAT DO I DO", NOT ONLY "HOW MUCH". A figure alone is what sent an administrator
/// through a pay run, a credits list and a database before finding out that six deactivated assignments
/// were the whole story. Each group therefore carries the assignment that would need reactivating.
/// </summary>
public sealed record GetPayeeUnreachableCommissionQuery(Guid PayeeId)
    : IRequest<Result<PayeeUnreachableCommissionDto>>;
