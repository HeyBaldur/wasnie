using Wasnie.Domain.Identity;

namespace Wasnie.Application.Features.Users.DTOs;

/// <summary>
/// One person with access to the tenant, as the users screen shows them.
///
/// ★ <c>IsActive</c> IS SENT, NOT THE TIMESTAMPS' MEANING. The screen needs a yes or no; the two
/// timestamps that produce it stay on the server, where the one rule that reads them lives
/// (<see cref="TenantUser.ActiveSpec"/>). Sending both and letting the client compare them would be a
/// second implementation of the rule, in a language that cannot be unit-tested against the first.
/// </summary>
public sealed record TenantUserDto(
    string UserId,
    string Email,
    string? FirstName,
    string? LastName,
    string? Role,
    bool IsActive,
    bool EmailConfirmed,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeactivatedAt,
    string? InvitedByEmail,
    /// <summary>
    /// KAN-93. The payee record this login owns, or null.
    ///
    /// ★★ THE SCREEN CANNOT OFFER THE FIX WITHOUT KNOWING THE STATE. The whole defect this closes is
    /// that a user with no payee is invisible as such: an administrator looking at the roster had no
    /// way to tell who was stranded, and therefore no reason to press anything. These two fields turn
    /// "ask an administrator" into something an administrator can actually see.
    ///
    /// ★ THE NAME TRAVELS WITH THE ID. The users screen holds no payee list — it would have to fetch
    /// one purely to render a name, and that second call could disagree with this one.
    /// </summary>
    Guid? LinkedPayeeId = null,
    string? LinkedPayeeName = null);

/// <summary>
/// An invitation in the list. <paramref name="Status"/> is computed at read time from the row and the
/// clock — there is no stored status to disagree with it.
/// </summary>
public sealed record InvitationDto(
    Guid Id,
    string Email,
    string Role,
    InvitationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt,
    int ResendCount,
    string? InvitedByEmail);

/// <summary>
/// The whole users screen in one response: the people, the outstanding invitations, and the seat
/// position.
///
/// ★★ ONE CALL, BECAUSE THE THREE MUST AGREE. Fetched separately, the seat count could be read a
/// second after an invitation was revoked in another tab and the page would show a used seat with
/// nothing using it. Here all three are computed inside one request against one clock.
/// </summary>
public sealed record TenantUsersResponse(
    IReadOnlyList<TenantUserDto> Users,
    IReadOnlyList<InvitationDto> Invitations,
    SeatUsageDto Seats);

/// <summary>
/// Seats used against seats allowed. <paramref name="Limit"/> null means unlimited, which is what
/// every plan ships with today (KAN-32).
/// </summary>
public sealed record SeatUsageDto(int Used, int ActiveUsers, int PendingInvitations, int? Limit, bool HasRoom);

/// <summary>
/// What the public accept page may know before anybody has signed in.
///
/// ★★ IT CARRIES NO IDENTIFIERS AND NO LIST OF ANYTHING. Whoever holds the link is, so far, an
/// anonymous stranger; they are told which company invited them and at which address, because that is
/// what makes the page legible and they already know the address — it is where the link arrived.
/// Nothing else about the tenant leaves the server on this route.
/// </summary>
public sealed record InvitationPreviewDto(string Email, string CompanyName, string InviterName);

/// <summary>A payee that could be attached to the person being invited.</summary>
public sealed record UnlinkedPayeeDto(Guid Id, string FullName, string? EmployeeCode, string? Email);

/// <summary>
/// One page of pickable payees, plus the one whose address matches — if any.
///
/// SUGGESTED IS A SUGGESTION AND NOTHING MORE. It is offered so the admin does not hunt through a
/// list of fifty names, and the form must PRE-SELECT NOTHING: a matching address is a hint, and a
/// wrong hint accepted by reflex shows one person another person's pay. The admin chooses.
///
/// ★★ <paramref name="Payees"/> IS A PAGE, NOT THE SET (KAN-93). It holds at most the handler's picker
/// limit and reflects whatever was searched for, so nothing may count it and conclude anything about
/// the workspace — that is what <paramref name="TotalAvailable"/> is for.
/// </summary>
/// <param name="TotalAvailable">
/// How many unlinked payees exist in total, IGNORING the search.
///
/// ★★ IT IS THE DIFFERENCE BETWEEN TWO OPPOSITE FACTS, which is why it is sent rather than derived.
/// Zero rows because every payee already belongs to a login is a problem the administrator fixes by
/// unlinking somebody; zero rows because they mistyped a name is one they fix by typing again. A
/// screen reading `Payees.Count == 0` would say the first when it meant the second (§B3).
/// </param>
public sealed record UnlinkedPayeesResponse(
    IReadOnlyList<UnlinkedPayeeDto> Payees,
    UnlinkedPayeeDto? Suggested,
    int TotalAvailable);
