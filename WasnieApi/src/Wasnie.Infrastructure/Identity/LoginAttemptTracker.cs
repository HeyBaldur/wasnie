using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Infrastructure.Identity;

/// <summary>
/// In-memory implementation of <see cref="ILoginAttemptTracker"/>.
///
/// Deliberately not persisted. The counter is keyed by whatever address was typed into the
/// login form, including addresses that belong to nobody — writing those to the database
/// would mean storing personal data of people who are not users, growing without bound, for
/// a signal whose whole lifetime is fifteen minutes.
///
/// The cost of that choice: a process restart forgets open windows, so a user whose account
/// is still locked by Identity sees the generic message again instead of the lockout notice.
/// The block itself is unaffected — only the explanation is lost.
/// </summary>
public sealed class LoginAttemptTracker(IMemoryCache cache, IClock clock) : ILoginAttemptTracker
{
    // Mirrors Identity's lockout settings (DependencyInjection.cs:321-323). The two must match:
    // if this window were shorter the notice would expire while the account was still blocked,
    // and if it were longer it would promise a wait that had already passed.
    internal const int MaxFailedAttempts = 5;
    internal static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);

    private const string KeyPrefix = "login-attempts:";

    private sealed class Entry
    {
        public int FailureCount;
        public DateTimeOffset? LockedUntil;
    }

    public LoginAttemptState RecordFailure(string email)
    {
        var key = KeyFor(email);
        var now = clock.UtcNowOffset;

        lock (cache)
        {
            var entry = cache.Get<Entry>(key) ?? new Entry();

            // Already inside an open window: do not extend it and do not re-report JustLocked.
            // This is what keeps a sustained attack from firing a warning email per attempt.
            if (entry.LockedUntil is { } until && until > now)
            {
                Store(key, entry, until);
                return new LoginAttemptState(true, JustLocked: false, RetryAfterMinutes: MinutesUntil(until, now));
            }

            // A window that has expired starts the count over.
            if (entry.LockedUntil is not null)
            {
                entry.FailureCount = 0;
                entry.LockedUntil = null;
            }

            entry.FailureCount++;

            if (entry.FailureCount < MaxFailedAttempts)
            {
                Store(key, entry, now.Add(LockoutWindow));
                return new LoginAttemptState(false, false, 0);
            }

            var lockedUntil = now.Add(LockoutWindow);
            entry.LockedUntil = lockedUntil;
            Store(key, entry, lockedUntil);
            return new LoginAttemptState(true, JustLocked: true, RetryAfterMinutes: MinutesUntil(lockedUntil, now));
        }
    }

    public void Reset(string email)
    {
        lock (cache)
        {
            cache.Remove(KeyFor(email));
        }
    }

    private void Store(string key, Entry entry, DateTimeOffset expiresAt) =>
        cache.Set(key, entry, new MemoryCacheEntryOptions { AbsoluteExpiration = expiresAt });

    private static int MinutesUntil(DateTimeOffset until, DateTimeOffset now) =>
        Math.Max(1, (int)Math.Ceiling((until - now).TotalMinutes));

    // Hashed so the cache never holds a readable address. Lower-cased first: the same person
    // typing a different case must land on the same counter, or five attempts become fifteen.
    private static string KeyFor(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return KeyPrefix + Convert.ToHexString(hash);
    }
}
