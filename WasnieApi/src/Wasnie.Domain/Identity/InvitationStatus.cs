namespace Wasnie.Domain.Identity;

/// <summary>
/// What an invitation looks like to a reader. Computed by <see cref="Invitation.StatusAt"/> — this
/// enum is never a column, so there is no row that can disagree with the clock.
/// </summary>
public enum InvitationStatus
{
    /// <summary>Sent, not yet used, not yet past its expiry.</summary>
    Pending = 0,

    /// <summary>Somebody opened the link and created their account.</summary>
    Accepted = 1,

    /// <summary>The deadline passed with nobody using it. Not a stored fact — the absence of one.</summary>
    Expired = 2,

    /// <summary>An admin withdrew it before it was used.</summary>
    Revoked = 3,
}
