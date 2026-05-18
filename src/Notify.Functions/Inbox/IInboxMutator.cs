using System.Net;
using Microsoft.Azure.Cosmos;

namespace Notify.Functions.Inbox;

// Mutates a single per-user inbox row in Cosmos. Behind an interface so the
// mutation handler stays unit-testable without the emulator. The partition key
// is the `source`; the doc id is `{baseId}:{userId}` (server-composed by
// Archive — see Notify.Shared.Cosmos.NotificationDocument.From).
public interface IInboxMutator
{
    Task<InboxMutateOutcome> MarkReadAsync(string source, string id, CancellationToken ct = default);
    Task<InboxMutateOutcome> MarkHiddenAsync(string source, string id, CancellationToken ct = default);
}

public enum InboxMutateOutcome
{
    Updated,
    NotFound,
}

public sealed class CosmosInboxMutator : IInboxMutator
{
    private readonly Container _notifications;

    public CosmosInboxMutator(Container notifications) => _notifications = notifications;

    public Task<InboxMutateOutcome> MarkReadAsync(string source, string id, CancellationToken ct = default)
        => PatchAsync(source, id, PatchOperation.Set("/isRead", true), ct);

    public Task<InboxMutateOutcome> MarkHiddenAsync(string source, string id, CancellationToken ct = default)
        => PatchAsync(source, id, PatchOperation.Set("/isHidden", true), ct);

    private async Task<InboxMutateOutcome> PatchAsync(string source, string id, PatchOperation op, CancellationToken ct)
    {
        try
        {
            await _notifications.PatchItemAsync<dynamic>(
                id,
                new PartitionKey(source),
                new[] { op },
                cancellationToken: ct);
            return InboxMutateOutcome.Updated;
        }
        catch (CosmosException ce) when (ce.StatusCode == HttpStatusCode.NotFound)
        {
            return InboxMutateOutcome.NotFound;
        }
    }
}
