using Wasnie.Domain.Common;

namespace Wasnie.Domain.Identity;

/// <summary>
/// AN OFFER OF A SEAT IN A TENANT, MADE TO AN EMAIL ADDRESS THAT MAY NOT HAVE AN ACCOUNT YET.
///
/// ★★ IT CARRIES ITS OWN <see cref="TenantId"/>, AND THAT IS NOT REDUNDANT. Everywhere else in this
/// system the tenant comes from the row's owner; here there is no owner yet. Tenant membership lives
/// in the <c>tenant_id</c> claim of an <c>IdentityUser</c> (there is no TenantId column on
/// AspNetUsers), and the whole point of an invitation is that the user does not exist until it is
/// accepted. So the invitation is the only thing holding the tenant between the send and the accept.
///
/// ★★ THE STATUS IS DERIVED, NOT STORED. <see cref="StatusAt"/> computes it from three timestamps
/// and the clock. A stored <c>Pending</c> would start lying the second <see cref="ExpiresAt"/> passed,
/// because nothing runs at that moment to change it — the classic flag that drifts (§B5). Accepted and
/// Revoked ARE stored, because those are facts that happened at a time somebody may need to see;
/// Expired is not a fact, it is the absence of one before a deadline.
///
/// ★ THE TOKEN IS STORED HASHED, mirroring <see cref="PasswordResetToken"/>: the raw value goes in the
/// email and is never written down. A leaked database therefore cannot be used to accept invitations.
/// Single use is <see cref="AcceptedAt"/>, not a separate flag.
/// </summary>
public sealed class Invitation : Entity
{
    public Guid TenantId { get; private set; }

    /// <summary>
    /// The address, trimmed and lower-cased.
    ///
    /// ★★ LOWER, NOT UPPER, AND IT IS A DISPLAY DECISION AS MUCH AS A MATCHING ONE. Case folding is
    /// what stops "Maria@x.com" and "maria@x.com" being two invitations; which direction it folds is
    /// free for matching and not free for the reader. The first cut folded upward and the pending
    /// list shouted MARIA.LOPEZ@NORDICSALES.EU at an admin who had typed it in lower case — the
    /// stored form reached the screen, as stored forms do (§C3).
    ///
    /// ★ THE DOMAIN IS CASE-INSENSITIVE BY RFC AND THE LOCAL PART TECHNICALLY IS NOT, which is the
    /// only argument for keeping the typed form in a second column. No mail provider in practice
    /// distinguishes them, and a second column is a second thing to keep in step (§B3), so the fold
    /// stays lossy and lower.
    /// </summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>What the person becomes on acceptance. Stored as the role name, as Identity holds it.</summary>
    public string Role { get; private set; } = string.Empty;

    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>The user id of whoever sent it — part of the audit answer "who let this person in".</summary>
    public string InvitedBy { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Set once, when the invitation becomes a user. Also what makes the token single-use.</summary>
    public DateTimeOffset? AcceptedAt { get; private set; }

    /// <summary>The user created by accepting this invitation. Null until then.</summary>
    public string? AcceptedUserId { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedBy { get; private set; }

    /// <summary>
    /// How many times it was sent again. A counter rather than a row per send, because the useful
    /// question is "has this person been chased" and not "when exactly was each attempt".
    /// </summary>
    public int ResendCount { get; private set; }

    public DateTimeOffset? LastSentAt { get; private set; }

    private Invitation() { }

    public static Invitation Create(
        Guid id,
        Guid tenantId,
        string email,
        string role,
        string tokenHash,
        string invitedBy,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("An invitation must belong to a tenant.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("An invitation must have an email.", nameof(email));
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("An invitation must have a role.", nameof(role));
        if (expiresAt <= now)
            throw new ArgumentException("An invitation cannot expire before it is created.", nameof(expiresAt));

        return new()
        {
            Id = id,
            TenantId = tenantId,
            Email = Normalise(email),
            Role = role.Trim(),
            TokenHash = tokenHash,
            InvitedBy = invitedBy,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            LastSentAt = now,
        };
    }

    /// <summary>The single spelling of an email, used for comparison AND for display.</summary>
    public static string Normalise(string email) => email.Trim().ToLowerInvariant();

    /// <summary>
    /// The state a reader should see. Derived on purpose — see the class remarks.
    /// </summary>
    public InvitationStatus StatusAt(DateTimeOffset now)
    {
        if (AcceptedAt is not null) return InvitationStatus.Accepted;
        if (RevokedAt is not null) return InvitationStatus.Revoked;
        return now >= ExpiresAt ? InvitationStatus.Expired : InvitationStatus.Pending;
    }

    /// <summary>Only a Pending invitation can be accepted, resent or revoked.</summary>
    public bool IsPendingAt(DateTimeOffset now) => StatusAt(now) == InvitationStatus.Pending;

    /// <summary>
    /// The same rule as <see cref="IsPendingAt"/>, written so EF can translate it to SQL.
    ///
    /// ★ ONE RULE, TWO CALLERS. The seat count and the "already invited" check both need to know what
    /// is outstanding, and a second hand-written predicate is how the count and the screen start
    /// disagreeing. The clock is a parameter rather than read inside, so a test can move it.
    /// </summary>
    public static System.Linq.Expressions.Expression<Func<Invitation, bool>> PendingSpec(DateTimeOffset now) =>
        i => i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now;

    public void Accept(string userId, DateTimeOffset now)
    {
        if (!IsPendingAt(now))
            throw new InvalidOperationException(
                $"An invitation that is {StatusAt(now)} cannot be accepted.");

        AcceptedAt = now;
        AcceptedUserId = userId;
    }

    public void Revoke(string revokedBy, DateTimeOffset now)
    {
        if (!IsPendingAt(now))
            throw new InvalidOperationException(
                $"An invitation that is {StatusAt(now)} cannot be revoked.");

        RevokedAt = now;
        RevokedBy = revokedBy;
    }

    /// <summary>
    /// Re-sends: a NEW token replaces the old one and the clock restarts.
    ///
    /// ★ THE OLD TOKEN STOPS WORKING, and that is the point. Leaving both alive would mean a link
    /// forwarded months ago still opens the door after the admin "renewed" the invitation, which is
    /// the opposite of what renewing looks like to the person doing it.
    /// </summary>
    public void Resend(string tokenHash, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (!IsPendingAt(now))
            throw new InvalidOperationException(
                $"An invitation that is {StatusAt(now)} cannot be resent.");

        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        ResendCount++;
        LastSentAt = now;
    }
}
