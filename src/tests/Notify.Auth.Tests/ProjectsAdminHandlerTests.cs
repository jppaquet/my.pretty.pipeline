using Microsoft.Azure.Cosmos;
using Notify.Functions.Admin;
using Notify.Shared.Cosmos;
using Notify.Shared.Hashing;

namespace Notify.Auth.Tests;

// Admin projects mint/list/revoke against the live emulator. Asserts the
// invariant that matters most: minted keys verify against the persisted
// hash via the same ApiKeyHasher the Ingest path consults — so admin-
// minted keys round-trip end-to-end.
[Trait("Category", "Integration")]
[Collection("Cosmos")]
public class ProjectsAdminHandlerTests
{
    private readonly CosmosEmulatorFixture _fx;

    public ProjectsAdminHandlerTests(CosmosEmulatorFixture fx) => _fx = fx;

    // Pepper for the test ApiKeyHasher. Stable across the class so a mint
    // in one test could (in principle) be verified by another instance.
    private static readonly byte[] TestPepper = "0123456789abcdef0123456789abcdef"u8.ToArray();

    // Shared container — see CosmosEmulatorFixture. Tests use unique
    // project ids per Mint to avoid stomping on each other. Creating one
    // container per test exhausts the emulator's
    // AZURE_COSMOS_EMULATOR_PARTITION_COUNT=10 and trips 503s.
    private ProjectsAdminHandler BuildHandler() =>
        new(_fx.Projects, new ApiKeyHasher(TestPepper));

    [Fact]
    public async Task Mint_returns_cleartext_key_in_npk_format()
    {
        var handler = BuildHandler();
        var result = await handler.MintAsync("test-mint", "Test Mint");

        var ok = Assert.IsType<ProjectMutationResult.OkWithKey>(result);
        Assert.StartsWith("npk_", ok.PlaintextKey);
        // 32 bytes -> 52 base32 chars; plus prefix "npk_" => 56.
        Assert.Equal(56, ok.PlaintextKey.Length);
        Assert.Equal("test-mint", ok.Project.Id);
        Assert.True(ok.Project.Active);
    }

    [Fact]
    public async Task Mint_persists_hash_that_round_trips_through_verify()
    {
        var hasher = new ApiKeyHasher(TestPepper);
        var handler = new ProjectsAdminHandler(_fx.Projects, hasher);

        var result = (ProjectMutationResult.OkWithKey)await handler.MintAsync("verify-roundtrip", "Roundtrip");
        var key = result.PlaintextKey;

        // Read the persisted doc directly and feed it back to Verify — this
        // is what the Ingest path does on every producer call.
        var read = await _fx.Projects.ReadItemAsync<ProjectDocument>("verify-roundtrip", new PartitionKey("verify-roundtrip"));
        Assert.True(hasher.Verify(key, read.Resource.Salt, read.Resource.KeyHash),
            "minted key should verify against the persisted salt + hash");
        Assert.False(hasher.Verify(key + "tampered", read.Resource.Salt, read.Resource.KeyHash));
    }

    [Fact]
    public async Task Mint_rejects_duplicate_id_with_already_exists()
    {
        var handler = BuildHandler();
        Assert.IsType<ProjectMutationResult.OkWithKey>(await handler.MintAsync("dup", "First"));
        Assert.IsType<ProjectMutationResult.AlreadyExists>(await handler.MintAsync("dup", "Second"));
    }

    [Theory]
    [InlineData("", "missing")]
    [InlineData("   ", "missing")]
    [InlineData("has spaces", "allowed chars")]
    [InlineData("has/slash", "allowed chars")]
    [InlineData("has#hash", "allowed chars")]
    public async Task Mint_rejects_invalid_project_ids(string id, string expectedSubstring)
    {
        var handler = BuildHandler();
        var result = await handler.MintAsync(id, "ignored");
        var inv = Assert.IsType<ProjectMutationResult.InvalidInput>(result);
        Assert.Equal("projectId", inv.Field);
        Assert.Contains(expectedSubstring, inv.Message);
    }

    [Fact]
    public async Task Mint_rejects_oversized_id()
    {
        var handler = BuildHandler();
        var tooLong = new string('a', 65);
        var inv = Assert.IsType<ProjectMutationResult.InvalidInput>(await handler.MintAsync(tooLong, "ok"));
        Assert.Equal("projectId", inv.Field);
    }

    [Fact]
    public async Task Mint_rejects_missing_display_name()
    {
        var handler = BuildHandler();
        var inv = Assert.IsType<ProjectMutationResult.InvalidInput>(await handler.MintAsync("ok-id", ""));
        Assert.Equal("displayName", inv.Field);
    }

    [Fact]
    public async Task List_returns_minted_projects_without_salt_or_hash()
    {
        var handler = BuildHandler();
        await handler.MintAsync("list-a", "A");
        await handler.MintAsync("list-b", "B");

        var items = await handler.ListAsync();
        var ids = items.Select(p => p.Id).ToHashSet();
        // Shared container: other tests may have already minted rows.
        // Assert containment, not exact equality.
        Assert.Contains("list-a", ids);
        Assert.Contains("list-b", ids);
        // ProjectSummary's shape doesn't carry salt / hash fields — that
        // invariant is enforced by the record itself (no constructor
        // arguments for them), so reaching this assertion is the proof.
    }

    [Fact]
    public async Task Revoke_flips_active_to_false()
    {
        var handler = BuildHandler();
        await handler.MintAsync("revoke-target", "RT");

        var result = await handler.RevokeAsync("revoke-target");
        var ok = Assert.IsType<ProjectMutationResult.Ok>(result);
        Assert.False(ok.Project.Active);

        // And the list reflects it.
        var items = await handler.ListAsync();
        Assert.False(items.Single(p => p.Id == "revoke-target").Active);
    }

    [Fact]
    public async Task Revoke_is_idempotent()
    {
        var handler = BuildHandler();
        await handler.MintAsync("revoke-twice", "RT");
        Assert.IsType<ProjectMutationResult.Ok>(await handler.RevokeAsync("revoke-twice"));
        Assert.IsType<ProjectMutationResult.Ok>(await handler.RevokeAsync("revoke-twice"));
    }

    [Fact]
    public async Task Revoke_on_unknown_id_returns_not_found()
    {
        var handler = BuildHandler();
        Assert.IsType<ProjectMutationResult.NotFound>(await handler.RevokeAsync("never-existed-" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public async Task Rotate_issues_a_new_key_that_verifies_and_invalidates_the_old()
    {
        var hasher = new ApiKeyHasher(TestPepper);
        var handler = new ProjectsAdminHandler(_fx.Projects, hasher);
        var minted = (ProjectMutationResult.OkWithKey)await handler.MintAsync("rotate-target", "RT");
        var oldKey = minted.PlaintextKey;

        var rotated = Assert.IsType<ProjectMutationResult.OkWithKey>(
            await handler.RotateAsync("rotate-target"));
        var newKey = rotated.PlaintextKey;

        Assert.NotEqual(oldKey, newKey);
        Assert.StartsWith("npk_", newKey);
        Assert.Equal("rotate-target", rotated.Project.Id);
        Assert.True(rotated.Project.Active);

        // The persisted doc now hashes the NEW key — old key no longer
        // verifies, new key does. This is what makes rotate a real
        // rotation, not a parallel-key issuance.
        var doc = (await _fx.Projects.ReadItemAsync<ProjectDocument>(
            "rotate-target", new PartitionKey("rotate-target"))).Resource;
        var salt = Convert.FromBase64String(doc.SaltBase64);
        var hash = Convert.FromBase64String(doc.KeyHashBase64);
        Assert.True(hasher.Verify(newKey, salt, hash));
        Assert.False(hasher.Verify(oldKey, salt, hash));
    }

    [Fact]
    public async Task Rotate_preserves_display_name_and_id()
    {
        var handler = BuildHandler();
        await handler.MintAsync("rotate-keep-shape", "Original Display");
        var rotated = (ProjectMutationResult.OkWithKey)await handler.RotateAsync("rotate-keep-shape");

        Assert.Equal("rotate-keep-shape", rotated.Project.Id);
        Assert.Equal("Original Display", rotated.Project.DisplayName);
    }

    [Fact]
    public async Task Rotate_preserves_revoked_state()
    {
        var handler = BuildHandler();
        await handler.MintAsync("rotate-revoked", "RR");
        await handler.RevokeAsync("rotate-revoked");

        var rotated = (ProjectMutationResult.OkWithKey)await handler.RotateAsync("rotate-revoked");

        // Rotating a revoked project gives a fresh inert key — the operator
        // can stage the new key now, then re-enable via a future unrevoke
        // path. Mixing rotate + unrevoke into one action would muddle audit
        // ("was this key used while revoked?"). The Ingest path still
        // short-circuits on `!project.Active`.
        Assert.False(rotated.Project.Active);
    }

    [Fact]
    public async Task Rotate_on_unknown_id_returns_not_found()
    {
        var handler = BuildHandler();
        Assert.IsType<ProjectMutationResult.NotFound>(
            await handler.RotateAsync("never-existed-" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public async Task Rotate_with_blank_id_is_invalid_input()
    {
        var handler = BuildHandler();
        var inv = Assert.IsType<ProjectMutationResult.InvalidInput>(await handler.RotateAsync(""));
        Assert.Equal("projectId", inv.Field);
    }
}
