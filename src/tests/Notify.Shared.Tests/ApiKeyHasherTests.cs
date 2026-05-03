using System.Security.Cryptography;
using Notify.Shared.Hashing;

namespace Notify.Shared.Tests;

public class ApiKeyHasherTests
{
    private static readonly byte[] Pepper = RandomNumberGenerator.GetBytes(32);
    private static readonly ApiKeyHasher Hasher = new(Pepper);

    [Fact]
    public void Hash_then_verify_roundtrips()
    {
        var key = "npk_abcdef0123456789abcdef0123456789";
        var salt = ApiKeyHasher.NewSalt();
        var hash = Hasher.Hash(key, salt);

        Assert.True(Hasher.Verify(key, salt, hash));
    }

    [Fact]
    public void Verify_rejects_wrong_key()
    {
        var salt = ApiKeyHasher.NewSalt();
        var hash = Hasher.Hash("npk_correct", salt);

        Assert.False(Hasher.Verify("npk_wrong", salt, hash));
    }

    [Fact]
    public void Verify_rejects_wrong_salt()
    {
        var saltA = ApiKeyHasher.NewSalt();
        var saltB = ApiKeyHasher.NewSalt();
        var hash = Hasher.Hash("npk_x", saltA);

        Assert.False(Hasher.Verify("npk_x", saltB, hash));
    }

    [Fact]
    public void Different_pepper_produces_different_hash()
    {
        var pepper2 = RandomNumberGenerator.GetBytes(32);
        var hasher2 = new ApiKeyHasher(pepper2);
        var salt = ApiKeyHasher.NewSalt();

        var hashA = Hasher.Hash("npk_x", salt);
        var hashB = hasher2.Hash("npk_x", salt);

        Assert.False(hashA.SequenceEqual(hashB));
    }

    [Fact]
    public void Hash_is_deterministic_for_same_inputs()
    {
        var salt = ApiKeyHasher.NewSalt();
        var a = Hasher.Hash("npk_x", salt);
        var b = Hasher.Hash("npk_x", salt);
        Assert.True(a.SequenceEqual(b));
    }

    [Fact]
    public void NewSalt_returns_independent_random_bytes()
    {
        var s1 = ApiKeyHasher.NewSalt();
        var s2 = ApiKeyHasher.NewSalt();
        Assert.Equal(ApiKeyHasher.SaltLength, s1.Length);
        Assert.False(s1.SequenceEqual(s2));
    }

    [Fact]
    public void Empty_pepper_throws()
    {
        Assert.Throws<ArgumentException>(() => new ApiKeyHasher(Array.Empty<byte>()));
    }

    [Fact]
    public void Wrong_salt_length_throws()
    {
        Assert.Throws<ArgumentException>(() => Hasher.Hash("npk_x", new byte[8]));
    }
}
