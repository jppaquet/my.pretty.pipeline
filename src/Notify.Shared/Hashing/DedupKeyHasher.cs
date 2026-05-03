using System.Security.Cryptography;
using System.Text;

namespace Notify.Shared.Hashing;

// Deterministic id derivation for deduplicated notifications.
// Same (source, deduplicationKey) → same Cosmos document id, so a re-send
// in the TTL window collides on Create and is swallowed as a 409 by Archive.
public static class DedupKeyHasher
{
    public static string Hash(string source, string deduplicationKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(deduplicationKey);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{source}:{deduplicationKey}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
