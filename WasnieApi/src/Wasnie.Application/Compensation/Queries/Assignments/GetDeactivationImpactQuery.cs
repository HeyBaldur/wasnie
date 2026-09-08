using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Assignments;

/// <summary>
/// How much unpaid commission would stop being payable if these assignments were deactivated.
///
/// ★★ IT EXISTS TO BE ASKED BEFORE THE ACT, NOT AFTER. Deactivating is silent and instant, and the
/// consequence only shows up later as a pay run that produces nothing: an administrator lost a day to
/// €385,731.02 that three screens agreed was owed and no run would pay, because six assignments had
/// been deactivated at some point nobody remembered. The confirmation dialog is the last moment where
/// saying the number is cheap.
///
/// ★ ONE QUERY FOR SINGLE AND BULK. The bulk path is the dangerous one — it is where six get switched
/// off in a click — so it must not be the path that gets the vaguer warning.
/// </summary>
public sealed record GetDeactivationImpactQuery(IReadOnlyList<Guid> AssignmentIds)
    : IRequest<Result<DeactivationImpactDto>>;
