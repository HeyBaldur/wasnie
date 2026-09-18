using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Users.Queries;

/// <summary>
/// The signed-in person's own figures (KAN-92, batch 3).
///
/// ★★ IT RESOLVES THE PAYEE FROM THE TOKEN, NOT FROM THE REQUEST. A caller who could name a payee id
/// would be a caller who could read anybody's pay; the only input is who they are, which they cannot
/// choose. The link comes from <c>Payee.UserId</c>, set deliberately when they were invited (batch 2).
/// </summary>
/// <param name="From">
/// KAN-98 — the window the reader is asking about, inclusive. Null means "no window": every figure that
/// CAN be period-scoped is then all-time, which is what this screen did before the range existed.
///
/// A PERSON NEEDS TO LOOK BACKWARDS AT THEIR OWN PAY. "How much was I paid two months ago" had no
/// answer here: the screen was pinned to all-time money and to the quotas in effect today, so a closed
/// period was unreachable — the one place where somebody would go to check a payment they remember
/// receiving. The administrator's dashboard has had a date range since KAN-62; this is the same
/// question asked about oneself.
///
/// IT IS STILL NOT AN IDENTIFIER. The window says WHICH DAYS, never WHOSE — the payee is resolved from
/// the token and from nothing else, and no combination of these two values reaches another person's
/// figures.
/// </param>
/// <param name="To">The inclusive upper bound. Half-open is allowed and means "from here on".</param>
public sealed record GetMyDashboardQuery(DateOnly? From = null, DateOnly? To = null)
    : IRequest<Result<MyDashboardDto>>;

/// <summary>
/// What a non-admin sees when they sign in.
///
/// ★★ <see cref="Linked"/> IS THE HONEST HALF. A person whose account was never attached to a payee
/// record has no figures — not zero, NONE — and the screen has to say which of the two it is. Showing
/// 0.00 to somebody who has actually earned money is the false zero this codebase already has a name
/// for, and it would arrive here dressed as a working page.
/// </summary>
/// <param name="SalesAwaitingSetup">
/// How many sales are recorded against this person that the engine CANNOT turn into commission yet.
///
/// ★★ WITHOUT IT, THEIR OWN SALE IS INVISIBLE TO THEM (KAN-94). A transaction that cannot be processed
/// produces no credit, no credit produces no payout, and every figure on this screen is built from
/// payouts — so somebody with a €5,000 sale to their name and no plan assignment saw a dashboard of
/// zeros and could only conclude one of two wrong things: that the product was broken, or that they
/// had sold nothing. The administrator could see the row all along (the "needs attention" card and the
/// Reconciliation Centre both count it); the person it was about could not.
///
/// ★★ A COUNT, AND DELIBERATELY NOT AN AMOUNT. Sending €5,000 here would put a large number on a pay
/// screen, and a large number on a pay screen is read as "I am owed this". What they are owed is
/// unknowable until a plan exists — it may be nothing — so the honest thing to send is the fact that
/// something is stuck, not a figure that implies a promise (§C3).
///
/// ★★ AND NOT THE REASON EITHER. Whether it is a missing assignment or a currency mismatch is the
/// tenant's configuration, which this reader cannot act on and should not have to understand. Their
/// one available action is the same in both cases: tell an administrator.
///
/// ★ ZERO IS A REAL ANSWER. It means every sale recorded against them has been processed, and the
/// screen shows no notice at all.
/// </param>
/// <param name="From">
/// KAN-98 — the window these figures were actually built over, echoed back.
///
/// IT IS ECHOED SO THE SCREEN CAN STATE IT RATHER THAN ASSUME IT. The client sends what it asked for;
/// only the server knows what was applied, and the two are not always the same value. A pay figure
/// whose period the reader has to infer from a control they may have moved since is a figure they will
/// eventually read against the wrong month.
///
/// Null on both ends means no window was applied and the period figures are all-time.
/// </param>
/// <param name="To">The upper bound actually applied, inclusive.</param>
public sealed record MyDashboardDto(
    bool Linked,
    Guid? PayeeId,
    string? PayeeName,
    PayeeLedgerSummaryDto? Summary,
    IReadOnlyList<MyQuotaAttainmentDto> Quotas,
    int SalesAwaitingSetup = 0,
    DateOnly? From = null,
    DateOnly? To = null);

/// <summary>
/// One quota of the signed-in person and how far along it is.
///
/// ★★ <see cref="AttainmentSource"/> TRAVELS WITH THE RATIO, because at zero the ratio alone is
/// ambiguous and the two readings are opposite: "sold nothing" and "nobody set a target". The engine
/// already refuses to conflate them (<c>AttainmentReading</c>), and a screen that dropped the
/// distinction would put the configuration hole back in front of the one person who cannot fix it.
///
/// ★★ <see cref="Measurement"/> IS WHAT THE TARGET IS COUNTED IN. A Units quota's target is a number
/// of deals and its currency means nothing; rendering "€40" over a target of 40 sales would be a
/// number lying about its own unit.
///
/// ★ THE FIGURES ARE NOT COMPUTED HERE. The ratio comes from the service the engine pays from and
/// <see cref="AchievedAmount"/> from the single shared definition of "achieved", so this screen and
/// the payslip cannot drift. A screen carrying its own credit-sum is what once showed 671% against a
/// true 336%.
/// </summary>
public sealed record MyQuotaAttainmentDto(
    Guid QuotaId,
    string PlanName,
    decimal TargetAmount,
    string Currency,
    string Measurement,
    decimal AchievedAmount,
    decimal AttainmentRatio,
    string AttainmentSource,
    DateOnly PeriodStart,
    DateOnly PeriodEnd);
