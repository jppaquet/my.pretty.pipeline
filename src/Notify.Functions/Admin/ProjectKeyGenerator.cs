using System.Security.Cryptography;

namespace Notify.Functions.Admin;

// Produces fresh producer API keys in the shape PROJECT-ONBOARDING.md
// already documents: `npk_` + lowercase RFC 4648 base32 over 32 random
// bytes, no padding. Match the bash recipe (`openssl rand 32 | base32 |
// tr -d '=' | tr '[:upper:]' '[:lower:]'`) byte-for-byte so the new admin
// mint flow produces keys indistinguishable from any minted manually
// before this code existed.
public static class ProjectKeyGenerator
{
    public const string KeyPrefix = "npk_";
    private const int RandomByteCount = 32;
    private const string Base32Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    public static string Mint()
    {
        Span<byte> bytes = stackalloc byte[RandomByteCount];
        RandomNumberGenerator.Fill(bytes);
        return KeyPrefix + Base32Encode(bytes);
    }

    // RFC 4648 base32 (lowercase, no padding). Output length is
    // ceil(input * 8 / 5) chars — 32 bytes → 52 chars.
    private static string Base32Encode(ReadOnlySpan<byte> input)
    {
        var outputLen = (input.Length * 8 + 4) / 5;
        var output = new char[outputLen];
        int bitBuffer = 0;
        int bitsInBuffer = 0;
        int outIndex = 0;
        foreach (var b in input)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitsInBuffer += 8;
            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                var index = (bitBuffer >> bitsInBuffer) & 0x1F;
                output[outIndex++] = Base32Alphabet[index];
            }
        }
        if (bitsInBuffer > 0)
        {
            var index = (bitBuffer << (5 - bitsInBuffer)) & 0x1F;
            output[outIndex++] = Base32Alphabet[index];
        }
        return new string(output, 0, outIndex);
    }
}
