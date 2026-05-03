using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Notify.Shared.Hashing;

// Argon2id hashing for producer API keys.
// - Per-key salt: 16 random bytes, stored in the projects Cosmos doc.
// - Subscription-wide pepper: 32 bytes, lives in Key Vault as `api-key-pepper`,
//   never in Cosmos. Compromise of the projects container alone can't recover keys.
// - Parameters from OWASP 2024 Argon2id baseline:
//     m=19 MiB, t=2, p=1, output=32 bytes.
//
// Verify is constant-time via CryptographicOperations.FixedTimeEquals.
public sealed class ApiKeyHasher
{
    public const int SaltLength = 16;
    public const int HashLength = 32;
    private const int MemorySizeKB = 19_456; // 19 MiB
    private const int Iterations = 2;
    private const int DegreeOfParallelism = 1;

    private readonly byte[] _pepper;

    public ApiKeyHasher(byte[] pepper)
    {
        ArgumentNullException.ThrowIfNull(pepper);
        if (pepper.Length == 0)
            throw new ArgumentException("Pepper must not be empty.", nameof(pepper));

        _pepper = pepper;
    }

    public static byte[] NewSalt()
    {
        var salt = new byte[SaltLength];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }

    public byte[] Hash(string apiKey, byte[] salt)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentNullException.ThrowIfNull(salt);
        if (salt.Length != SaltLength)
            throw new ArgumentException($"Salt must be {SaltLength} bytes.", nameof(salt));

        using var argon = new Argon2id(Encoding.UTF8.GetBytes(apiKey))
        {
            Salt = salt,
            KnownSecret = _pepper,
            DegreeOfParallelism = DegreeOfParallelism,
            MemorySize = MemorySizeKB,
            Iterations = Iterations,
        };
        return argon.GetBytes(HashLength);
    }

    public bool Verify(string apiKey, byte[] salt, byte[] expectedHash)
    {
        ArgumentNullException.ThrowIfNull(expectedHash);
        if (expectedHash.Length != HashLength)
            return false;

        var actual = Hash(apiKey, salt);
        return CryptographicOperations.FixedTimeEquals(actual, expectedHash);
    }
}
