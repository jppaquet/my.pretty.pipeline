using Azure.Messaging;
using Azure.Messaging.EventGrid;
using Notify.Shared.CloudEvents;
using Notify.Shared.Json;

namespace Notify.Functions.Ingestion;

// Wrap EventGrid publishing behind an interface so unit + integration tests
// can record published envelopes without standing up a real EG endpoint.
public interface IEventPublisher
{
    Task PublishAsync(CloudEventEnvelope envelope, CancellationToken ct = default);
    Task PublishBatchAsync(IReadOnlyList<CloudEventEnvelope> envelopes, CancellationToken ct = default);
}

public sealed class EventGridEventPublisher : IEventPublisher
{
    private readonly EventGridPublisherClient _client;

    public EventGridEventPublisher(EventGridPublisherClient client) => _client = client;

    public Task PublishAsync(CloudEventEnvelope envelope, CancellationToken ct = default)
        => _client.SendEventAsync(ToCloudEvent(envelope), ct);

    public Task PublishBatchAsync(IReadOnlyList<CloudEventEnvelope> envelopes, CancellationToken ct = default)
    {
        // SendEventsAsync is one HTTP request to EventGrid; EG accepts the
        // batch atomically (all events accepted or 4xx with none accepted).
        // No partial-state to reconcile in the handler.
        var ces = new CloudEvent[envelopes.Count];
        for (var i = 0; i < envelopes.Count; i++) ces[i] = ToCloudEvent(envelopes[i]);
        return _client.SendEventsAsync(ces, ct);
    }

    private static CloudEvent ToCloudEvent(CloudEventEnvelope envelope)
    {
        // The 3-arg CloudEvent(source, type, object) ctor binds to the
        // (source, type, object, Type?) overload and *throws* at runtime when it
        // sees the object is a BinaryData: "This constructor does not support
        // BinaryData. Use the constructor that takes a BinaryData instance."
        // Use the explicit 4-arg BinaryData overload — dataContentType is
        // required so the broker tags the event correctly.
        return new CloudEvent(
            envelope.Source,
            envelope.Type,
            BinaryData.FromObjectAsJson(envelope.Data, NotifyJson.Options),
            "application/json",
            CloudEventDataFormat.Json)
        {
            Id = envelope.Id,
            Time = envelope.Time,
        };
    }
}
