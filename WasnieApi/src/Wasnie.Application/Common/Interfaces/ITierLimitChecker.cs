namespace Wasnie.Application.Common.Interfaces;

public interface ITierLimitChecker
{
    Task EnsurePayeeLimitAsync(CancellationToken cancellationToken = default);
    Task EnsurePlanLimitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether importing <paramref name="incomingCount"/> payees would exceed the tenant's tier limit.
    /// Does NOT throw — returns structured result so callers can return domain-specific HTTP responses.
    /// Logs the denial to the audit trail if blocked.
    /// </summary>
    Task<PayeeImportLimitCheck> CheckPayeeImportLimitAsync(int incomingCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many seats this tenant is using and how many it may use (KAN-32).
    ///
    /// ★ IT DOES NOT THROW, because the ticket asks the admin to be warned BEFORE an email goes out.
    /// A check that could only refuse would leave the screen guessing at the numbers it has to show.
    /// </summary>
    Task<SeatUsage> GetSeatUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Refuses when there is no seat left. The guard on the write path, beside the warning above.
    /// </summary>
    Task EnsureSeatAvailableAsync(CancellationToken cancellationToken = default);
}

/// <summary>Result of a bulk-import tier limit pre-flight check.</summary>
public sealed record PayeeImportLimitCheck(bool Blocked, int Current, int Limit, string Tier);

/// <summary>
/// What the tenant is using against its seat allowance.
///
/// ★★ USED COUNTS OUTSTANDING INVITATIONS AS WELL AS ACTIVE USERS, which is a decision and not an
/// accident. The ticket says a seat is consumed on acceptance; taken literally, a tenant with one seat
/// left could send fifty invitations and fifty people would be refused at the moment they set their
/// password. Reserving the seat when the invitation goes out moves that refusal to the admin, who can
/// do something about it, and is the only reading under which "warn before sending, with count and
/// limit" means anything. A revoked or expired invitation releases its seat, because PendingSpec stops
/// matching it.
///
/// ★ LIMIT IS NULLABLE AND NULL MEANS UNLIMITED — today, every plan. <see cref="HasRoom"/> is derived
/// from the two numbers rather than stored, so nothing can claim to be full while the count says
/// otherwise.
/// </summary>
public sealed record SeatUsage(int ActiveUsers, int PendingInvitations, int? Limit, string Tier)
{
    /// <summary>Seats consumed: people who can sign in, plus invitations still outstanding.</summary>
    public int Used => ActiveUsers + PendingInvitations;

    public bool HasRoom => Limit is not int limit || Used < limit;
}
