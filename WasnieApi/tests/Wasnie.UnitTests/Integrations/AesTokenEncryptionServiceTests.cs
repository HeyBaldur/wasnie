using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Options;
using Wasnie.Infrastructure.Services.HubSpot;

namespace Wasnie.UnitTests.Integrations;

public sealed class AesTokenEncryptionServiceTests
{
    private const string Key = "vLGER65fH1O7Nt7nbwTTC2FKKUJ+hMADby9FwcmxihE="; // 32 bytes (base64)

    private static AesTokenEncryptionService Create(string key = Key) =>
        new(Options.Create(new HubSpotOptions { TokenEncryptionKey = key }));

    [Fact]
    public void Encrypt_then_Decrypt_round_trips()
    {
        var svc = Create();
        const string plaintext = "CJ1-test-access-token-value";

        var cipher = svc.Encrypt(plaintext);
        svc.Decrypt(cipher).Should().Be(plaintext);
    }

    [Fact]
    public void Ciphertext_is_not_plaintext()
    {
        var svc = Create();
        const string plaintext = "super-secret-refresh-token";

        var cipher = svc.Encrypt(plaintext);

        cipher.Should().NotBe(plaintext);
        cipher.Should().NotContain(plaintext);
    }

    [Fact]
    public void Encrypting_same_value_twice_yields_different_ciphertext()
    {
        // Fresh random nonce per call → no deterministic leakage.
        var svc = Create();
        svc.Encrypt("same").Should().NotBe(svc.Encrypt("same"));
    }

    [Fact]
    public void Decrypt_with_wrong_key_fails()
    {
        var cipher = Create().Encrypt("secret");
        var otherKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var act = () => Create(otherKey).Decrypt(cipher);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Tampered_ciphertext_is_rejected()
    {
        var svc = Create();
        var cipher = svc.Encrypt("secret");

        var bytes = Convert.FromBase64String(cipher);
        bytes[^1] ^= 0xFF; // flip last byte of ciphertext
        var tampered = Convert.ToBase64String(bytes);

        var act = () => svc.Decrypt(tampered);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Missing_or_invalid_key_throws_on_construction()
    {
        var act = () => Create(string.Empty);
        act.Should().Throw<InvalidOperationException>();

        var shortKey = Convert.ToBase64String(new byte[16]);
        var act2 = () => Create(shortKey);
        act2.Should().Throw<InvalidOperationException>();
    }
}
