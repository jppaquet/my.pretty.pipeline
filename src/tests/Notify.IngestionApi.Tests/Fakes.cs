using Notify.Functions.Ingestion;
using Notify.Shared.CloudEvents;
using Notify.Shared.Cosmos;

namespace Notify.Functions.Ingestion.Tests;

internal sealed class InMemoryProjectLookup : IProjectLookup
{
    public Dictionary<string, ProjectDocument> Projects { get; } = new();
    public Task<ProjectDocument?> FindAsync(string projectId, CancellationToken ct = default)
        => Task.FromResult(Projects.GetValueOrDefault(projectId));
}

internal sealed class RecordingPublisher : IEventPublisher
{
    public List<CloudEventEnvelope> Published { get; } = new();
    public Task PublishAsync(CloudEventEnvelope envelope, CancellationToken ct = default)
    {
        Published.Add(envelope);
        return Task.CompletedTask;
    }
}
