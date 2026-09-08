namespace Wasnie.Application.Common.Interfaces;

/// <summary>
/// Counts failed login attempts per email address so the login response can tell the user
/// their access is temporarily blocked.
///
/// Why this exists alongside Identity's own lockout: Identity only counts attempts against
/// accounts that exist. Deciding the on-screen message from Identity's lockout state would
/// therefore leak whether an address is registered — five attempts and the message itself
/// answers the question. This tracker counts every failure, for any address, so the response
/// is identical whether or not the account exists.
///
/// It decides only the MESSAGE. The actual block is still Identity's lockout; nothing here
/// grants or denies access.
/// </summary>
public interface ILoginAttemptTracker
{
    /// <summary>
    /// Records one failed attempt and returns the resulting state.
    /// Attempts made while a window is already open do not extend it and never report
    /// <see cref="LoginAttemptState.JustLocked"/> a second time.
    /// </summary>
    LoginAttemptState RecordFailure(string email);

    /// <summary>Clears the counter. Called on a successful sign-in.</summary>
    void Reset(string email);
}

/// <param name="IsLocked">A lockout window is currently open for this address.</param>
/// <param name="JustLocked">
/// This very attempt opened the window. True exactly once per window — it is what keeps the
/// warning email to one per lockout without storing a "already notified" flag that could
/// drift out of step with the window itself.
/// </param>
/// <param name="RetryAfterMinutes">Whole minutes until the window closes; 0 when not locked.</param>
public readonly record struct LoginAttemptState(bool IsLocked, bool JustLocked, int RetryAfterMinutes);
