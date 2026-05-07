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
}

public sealed class EventGridEventPublisher : IEventPublisher
{
    private readonly EventGridPublisherClient _client;

    public EventGridEventPublisher(EventGridPublisherClient client) => _client = client;

    public Task PublishAsync(CloudEventEnvelope envelope, CancellationToken ct = default)
    {
        var ce = new CloudEvent(envelope.Source, envelope.Type, BinaryData.FromObjectAsJson(envelope.Data, NotifyJson.Options))
        {
            Id = envelope.Id,
            Time = envelope.Time,
            DataSchema = envelope.DataContentType,
        };
        return _client.SendEventAsync(ce, ct);
    }
}
