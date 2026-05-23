using System.Text.Json;
using Notify.Shared;
using Notify.Shared.CloudEvents;
using Notify.Shared.Json;

namespace Notify.Functions.Push;

// APNs payload + apns-priority header bundle. Built from a NotifyCreatedV1.
//
// APNs has a hard 4 KB limit on the total payload. Body alone is bounded
// at 2 KB by the validator (BodyMaxChars), which leaves comfortable
// headroom for the wrapper + identifiers when only `body` is carried.
//
// `metadata` is intentionally NOT included here. The schema's `metadata`
// field (e.g. `fullBody` for long-form digest content) is sized to the
// inbox API contract (32 KB cap, per #160) — way over the 4 KB APNs
// limit. Pre-#160 this worked because metadata was capped at 4 KB; now
// stuffing it into the push payload trips
//   `Microsoft.Azure.NotificationHubs.Messaging.BadRequestException:
//    Notification payload is too large. Actual Length: 'NNNN' and
//    Max Allowed Length: '4096'`
// on every Google Alert with a real-size digest, dropping the user's push.
//
// The iOS app reads metadata.fullBody from the inbox API on open / via
// the inbox refresh — it doesn't need the metadata in the push payload.
// The push payload's job is the lock-screen banner (title + body), the
// deeplink, and the keys the app uses to route the tap (id, source,
// type). All bounded; sum well under 4 KB.
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

        var json = JsonSerializer.Serialize(root, NotifyJson.Options);
        return new ApnsPayload(json, message.Priority.ToApnsPriority());
    }
}
