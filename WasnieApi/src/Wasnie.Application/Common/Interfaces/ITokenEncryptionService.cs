namespace Wasnie.Application.Common.Interfaces;

/// <summary>
/// Encrypts/decrypts sensitive strings (OAuth tokens) for storage at rest.
/// Implementations MUST be authenticated encryption (tamper-evident) and MUST NOT log plaintext.
/// </summary>
public interface ITokenEncryptionService
{
    /// <summary>Encrypts plaintext, returning an opaque self-describing string safe to persist.</summary>
    string Encrypt(string plaintext);

    /// <summary>Decrypts a value previously produced by <see cref="Encrypt"/>.</summary>
    string Decrypt(string ciphertext);
}
