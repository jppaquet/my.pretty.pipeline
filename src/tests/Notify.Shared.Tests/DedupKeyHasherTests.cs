using Notify.Shared.Hashing;

namespace Notify.Shared.Tests;

public class DedupKeyHasherTests
{
    [Fact]
    public void Same_inputs_produce_same_hash()
    {
        var a = DedupKeyHasher.Hash("home-pipeline", "backup-2026-04-28");
        var b = DedupKeyHasher.Hash("home-pipeline", "backup-2026-04-28");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_source_produces_different_hash()
    {
        var a = DedupKeyHasher.Hash("home-pipeline", "k");
        var b = DedupKeyHasher.Hash("ci-pipeline", "k");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Different_dedup_key_produces_different_hash()
    {
        var a = DedupKeyHasher.Hash("s", "k1");
        var b = DedupKeyHasher.Hash("s", "k2");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Hash_is_lowercase_hex_64_chars()
    {
        var h = DedupKeyHasher.Hash("s", "k");
        Assert.Equal(64, h.Length);
        Assert.Matches("^[0-9a-f]{64}$", h);
    }

    [Theory]
    [InlineData("", "k")]
    [InlineData("s", "")]
    public void Empty_inputs_throw(string source, string dedup)
    {
        Assert.Throws<ArgumentException>(() => DedupKeyHasher.Hash(source, dedup));
    }
}
