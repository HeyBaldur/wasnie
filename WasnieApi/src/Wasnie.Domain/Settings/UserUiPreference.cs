using System.Text.RegularExpressions;
using Wasnie.Domain.Common;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Settings;

/// <summary>
/// One remembered UI choice of one user: "saw the welcome", "dismissed the sandbox intro", "snoozed the 2FA reminder
/// until …" (KAN-78).
///
/// ★ A PROPERTY OF THE USER, NOT OF THE BROWSER. These used to live in localStorage, which does not know who is signed
/// in, is wiped on logout (on purpose, for shared machines) and does not travel to another machine — so every
/// modal came back after switching users. Stored here, the choice follows the person.
///
/// ★ GENERIC ON PURPOSE. A new modal adds a KEY, not a table or an endpoint. The value is opaque to the server (a small
/// JSON document the client owns); the server only bounds its size and the shape of the key.
///
/// ★ OWNERSHIP IS (TenantId, UserId), like AssistantConversation: every read matches both, so two users of one tenant
/// never see each other's choices.
/// </summary>
public sealed partial class UserUiPreference : Entity
{
    public const int MaxKeyLength = 64;

    /// <summary>A flag or a date is a few bytes; the bound only stops the table being used as a blob store.</summary>
    public const int MaxValueLength = 2000;

    public Guid TenantId { get; private set; }

    /// <summary>ASP.NET Identity user id (450, same width as the other user-owned tables).</summary>
    public string UserId { get; private set; } = string.Empty;

    public string Key { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; private set; }

    private UserUiPreference() { }

    public static UserUiPreference Create(Guid id, Guid tenantId, string userId, string key, string value, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("TenantId must not be empty.");
        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("UserId must not be empty.");
        EnsureValidKey(key);

        var preference = new UserUiPreference { Id = id, TenantId = tenantId, UserId = userId, Key = key };
        preference.SetValue(value, now);
        return preference;
    }

    public void SetValue(string value, DateTimeOffset now)
    {
        EnsureValidValue(value);
        Value = value;
        UpdatedAt = now;
    }

    public static bool IsValidKey(string? key) => key is not null && KeyPattern().IsMatch(key);

    private static void EnsureValidKey(string key)
    {
        if (!IsValidKey(key))
            throw new DomainException($"A preference key is 1-{MaxKeyLength} lowercase letters, digits, dots or dashes.");
    }

    private static void EnsureValidValue(string value)
    {
        if (value is null)
            throw new DomainException("A preference value is required.");
        if (value.Length > MaxValueLength)
            throw new DomainException($"A preference value must be at most {MaxValueLength} characters.");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{0,63}$")]
    private static partial Regex KeyPattern();
}
