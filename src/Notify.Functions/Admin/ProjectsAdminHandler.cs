using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Azure.Cosmos;
using Notify.Shared.Cosmos;
using Notify.Shared.Hashing;

namespace Notify.Functions.Admin;

// Result types for the projects admin plane. Mint returns the cleartext key
// exactly once — it's not stored anywhere (the doc only carries the salt +
// argon2id hash). Callers must surface this to the user immediately and
// drop the value; there is no recovery if it's lost.
public sealed record ProjectSummary(string Id, string DisplayName, bool Active);

public sealed record MintProjectResult(ProjectSummary Project, string PlaintextKey);

public abstract record ProjectMutationResult
{
    public sealed record Ok(ProjectSummary Project) : ProjectMutationResult;
    public sealed record OkWithKey(ProjectSummary Project, string PlaintextKey) : ProjectMutationResult;
    public sealed record NotFound : ProjectMutationResult;
    public sealed record AlreadyExists : ProjectMutationResult;
    public sealed record InvalidInput(string Field, string Message) : ProjectMutationResult;
}

public sealed partial class ProjectsAdminHandler
{
    [GeneratedRegex(@"^[A-Za-z0-9._-]+$")] private static partial Regex IdRegex();

    private const int MaxIdLength = 64;
    private const int MaxDisplayNameLength = 128;

    private readonly Container _projects;
    private readonly ApiKeyHasher _hasher;

    public ProjectsAdminHandler(Container projects, ApiKeyHasher hasher)
    {
        _projects = projects;
        _hasher = hasher;
    }

    public async Task<IReadOnlyList<ProjectSummary>> ListAsync(CancellationToken ct = default)
    {
        // SELECT only the public-facing fields; salt + keyHash never leave
        // the function process even toward an authenticated admin. The
        // hash being slow to crack is a defense-in-depth property — exposing
        // it would erase that margin.
        var query = new QueryDefinition(
            "SELECT c.projectId, c.displayName, c.active FROM c ORDER BY c.projectId");
        var iter = _projects.GetItemQueryIterator<ProjectRow>(query);
        var items = new List<ProjectSummary>();
        while (iter.HasMoreResults)
        {
            foreach (var row in await iter.ReadNextAsync(ct))
                items.Add(new ProjectSummary(row.ProjectId, row.DisplayName, row.Active));
        }
        return items;
    }

    public async Task<ProjectMutationResult> MintAsync(string projectId, string displayName, CancellationToken ct = default)
    {
        var validation = ValidateInput(projectId, displayName);
        if (validation is not null) return validation;

        var key = ProjectKeyGenerator.Mint();
        var salt = ApiKeyHasher.NewSalt();
        var hash = _hasher.Hash(key, salt);

        var doc = new ProjectDocument
        {
            Id = projectId,
            ProjectId = projectId,
            DisplayName = displayName,
            SaltBase64 = Convert.ToBase64String(salt),
            KeyHashBase64 = Convert.ToBase64String(hash),
            Active = true,
        };

        try
        {
            await _projects.CreateItemAsync(doc, new PartitionKey(projectId), cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return new ProjectMutationResult.AlreadyExists();
        }

        return new ProjectMutationResult.OkWithKey(
            new ProjectSummary(projectId, displayName, true), key);
    }

    public async Task<ProjectMutationResult> RevokeAsync(string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return new ProjectMutationResult.InvalidInput("projectId", "missing");

        ProjectDocument current;
        try
        {
            var read = await _projects.ReadItemAsync<ProjectDocument>(
                projectId, new PartitionKey(projectId), cancellationToken: ct);
            current = read.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new ProjectMutationResult.NotFound();
        }

        // Revoke is keyHash-preserving: an admin who reads the cosmos doc
        // post-revoke can't recover the original key, but rotating back to
        // active doesn't require re-minting if the operator decides revoke
        // was a mistake. If a future requirement says "revoke must
        // invalidate the key permanently", overwrite SaltBase64 + KeyHashBase64
        // here.
        var updated = new ProjectDocument
        {
            Id = current.Id,
            ProjectId = current.ProjectId,
            DisplayName = current.DisplayName,
            SaltBase64 = current.SaltBase64,
            KeyHashBase64 = current.KeyHashBase64,
            Active = false,
        };
        await _projects.ReplaceItemAsync(updated, projectId, new PartitionKey(projectId), cancellationToken: ct);
        return new ProjectMutationResult.Ok(
            new ProjectSummary(updated.ProjectId, updated.DisplayName, false));
    }

    private static ProjectMutationResult? ValidateInput(string projectId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return new ProjectMutationResult.InvalidInput("projectId", "missing");
        if (projectId.Length > MaxIdLength)
            return new ProjectMutationResult.InvalidInput("projectId", $"must be at most {MaxIdLength} chars");
        if (!IdRegex().IsMatch(projectId))
            return new ProjectMutationResult.InvalidInput("projectId", "allowed chars: A-Z, a-z, 0-9, '.', '_', '-'");
        if (string.IsNullOrWhiteSpace(displayName))
            return new ProjectMutationResult.InvalidInput("displayName", "missing");
        if (displayName.Length > MaxDisplayNameLength)
            return new ProjectMutationResult.InvalidInput("displayName", $"must be at most {MaxDisplayNameLength} chars");
        return null;
    }

    // Internal projection used for the list query; spares the response from
    // ever materializing the salt/hash fields even if the JSON serializer
    // gets pointed at the wrong type.
    private sealed record ProjectRow(string ProjectId, string DisplayName, bool Active);
}
