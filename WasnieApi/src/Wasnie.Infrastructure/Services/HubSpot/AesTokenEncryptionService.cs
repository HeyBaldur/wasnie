using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;

namespace Wasnie.Infrastructure.Services.HubSpot;

/// <summary>
/// Authenticated encryption at rest for OAuth tokens using AES-256-GCM.
///
/// The 256-bit key comes from <c>HubSpot:TokenEncryptionKey</c> (base64), which lives ONLY in gitignored
/// config (dev) or a secret store (prod). Each value gets a fresh random 96-bit nonce; the stored blob is
/// base64( nonce(12) || tag(16) || ciphertext ). GCM authenticates the data, so tampering is detected on
/// decrypt.
///
/// PRODUCTION NOTE: a static config key is acceptable for dev. In production this should use envelope
/// encryption with a KMS (e.g. AWS KMS / Azure Key Vault) — the KMS holds the master key and the data key
/// is wrapped per record — rather than a single long-lived key in config. This service is the swap point.
/// </summary>
public sealed class AesTokenEncryptionService : ITokenEncryptionService
{
    private const int NonceSize = 12; // 96-bit nonce (GCM standard)
    private const int TagSize = 16;   // 128-bit auth tag
    private const int KeySize = 32;   // 256-bit key

    private readonly byte[] _key;

    public AesTokenEncryptionService(IOptions<HubSpotOptions> options)
    {
        var configured = options.Value.TokenEncryptionKey;
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                "HubSpot:TokenEncryptionKey is not configured. Set a base64-encoded 32-byte key in gitignored config.");

        byte[] key;
        try { key = Convert.FromBase64String(configured); }
        catch (FormatException)
        {
            throw new InvalidOperationException("HubSpot:TokenEncryptionKey must be valid base64.");
        }

        if (key.Length != KeySize)
            throw new InvalidOperationException(
                $"HubSpot:TokenEncryptionKey must decode to exactly {KeySize} bytes (256-bit). Got {key.Length}.");

        _key = key;
    }

    public string Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, cipher, tag);

        var blob = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, blob, NonceSize + TagSize, cipher.Length);

        return Convert.ToBase64String(blob);
    }

    public string Decrypt(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrEmpty(ciphertext);

        var blob = Convert.FromBase64String(ciphertext);
        if (blob.Length < NonceSize + TagSize)
            throw new CryptographicException("Ciphertext is malformed.");

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipher = new byte[blob.Length - NonceSize - TagSize];
        Buffer.BlockCopy(blob, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(blob, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(blob, NonceSize + TagSize, cipher, 0, cipher.Length);

        var plaintext = new byte[cipher.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}
