using System.Text.Json;
using Notify.Shared;
using Notify.Shared.CloudEvents;
using Notify.Shared.Json;

namespace Notify.PushDelivery.Push;

// APNs payload + apns-priority header bundle. Built from a NotifyCreatedV1.
// We don't truncate the body here — the validator caps at 2 KB which is
// already well under APNs' 4 KB total payload limit, and we want producers
// to see oversize as a 400 from IngestionApi rather than silent truncation
// downstream.
public sealed record ApnsPayload(string Json, int ApnsPriority)
{
    public static ApnsPayload From(NotifyCreatedV1 message)
    {
        var aps = new Dictionary<string, object?>
        {
            ["alert"] = new
            {
                title = message.Title,
                body = message.Body,
            },
            ["sound"] = "default",
        };

        var root = new Dictionary<string, object?>
        {
            ["aps"] = aps,
            ["id"] = message.Id,
            ["source"] = message.Source,
            ["type"] = message.Type,
        };

        if (!string.IsNullOrEmpty(message.Deeplink))
            root["deeplink"] = message.Deeplink;

        if (message.Metadata is not null && message.Metadata.Count > 0)
            root["metadata"] = message.Metadata;

        var json = JsonSerializer.Serialize(root, NotifyJson.Options);
        return new ApnsPayload(json, message.Priority.ToApnsPriority());
    }
}
