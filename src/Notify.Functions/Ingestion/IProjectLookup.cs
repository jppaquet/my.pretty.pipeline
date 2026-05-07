using System.Net;
using Microsoft.Azure.Cosmos;
using Notify.Shared.Cosmos;

namespace Notify.Functions.Ingestion;

// Seam between IngestHandler and Cosmos so unit tests don't need an emulator.
public interface IProjectLookup
{
    Task<ProjectDocument?> FindAsync(string projectId, CancellationToken ct = default);
}

public sealed class CosmosProjectLookup : IProjectLookup
{
    private readonly Container _projects;

    public CosmosProjectLookup(Container projects) => _projects = projects;

    public async Task<ProjectDocument?> FindAsync(string projectId, CancellationToken ct = default)
    {
        try
        {
            var read = await _projects.ReadItemAsync<ProjectDocument>(
                projectId, new PartitionKey(projectId), cancellationToken: ct);
            return read.Resource;
        }
        catch (CosmosException ce) when (ce.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
