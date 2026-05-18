using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Azure.Messaging;
using Notify.Shared.CloudEvents;
using Notify.Shared.Json;

namespace Notify.Functions.Ingestion;

// Parses CloudEvents 1.0 HTTP requests (structured, batch, binary) into
// IngestInput records. Validates the envelope (specversion, type,
// datacontenttype, required attributes) and the data payload's JSON shape.
// Application-layer validation of the payload runs later in IngestHandler.
public static class CloudEventsParser
{
    public const string ExpectedType = CloudEventEnvelope.CurrentType;
    public const string StructuredContentType = "application/cloudevents+json";
    public const string BatchContentType = "application/cloudevents-batch+json";

    public static bool TryParseStructured(
        BinaryData body,
        [NotNullWhen(true)] out IngestInput? input,
        [NotNullWhen(false)] out string? error)
    {
        input = null;
        CloudEvent? ce;
        try { ce = CloudEvent.Parse(body); }
        catch (Exception ex) { error = $"malformed CloudEvent: {ex.Message}"; return false; }
        if (ce is null) { error = "empty CloudEvent envelope"; return false; }
        return TryFromCloudEvent(ce, out input, out error);
    }

    public static bool TryParseBatch(
        BinaryData body,
        [NotNullWhen(true)] out IReadOnlyList<IngestInput>? inputs,
        [NotNullWhen(false)] out string? error)
    {
        inputs = null;
        CloudEvent[]? ces;
        try { ces = CloudEvent.ParseMany(body); }
        catch (Exception ex) { error = $"malformed CloudEvents batch: {ex.Message}"; return false; }
        if (ces is null || ces.Length == 0) { error = "empty CloudEvents batch"; return false; }

        var list = new IngestInput[ces.Length];
        for (var i = 0; i < ces.Length; i++)
        {
            if (!TryFromCloudEvent(ces[i], out var inp, out var perr))
            { error = $"events[{i}]: {perr}"; return false; }
            list[i] = inp;
        }
        inputs = list;
        error = null;
        return true;
    }

    public static bool TryParseBinary(
        Func<string, string?> getHeader,
        string? requestContentType,
        BinaryData body,
        [NotNullWhen(true)] out IngestInput? input,
        [NotNullWhen(false)] out string? error)
    {
        input = null;

        var specversion = getHeader("ce-specversion");
        if (string.IsNullOrWhiteSpace(specversion))
        { error = "missing ce-specversion header"; return false; }
        if (specversion != "1.0")
        { error = $"ce-specversion must be '1.0', was '{specversion}'"; return false; }

        var type = getHeader("ce-type");
        if (!string.Equals(type, ExpectedType, StringComparison.Ordinal))
        { error = $"ce-type must be '{ExpectedType}', was '{type ?? "<missing>"}'"; return false; }

        var source = getHeader("ce-source");
        if (string.IsNullOrWhiteSpace(source))
        { error = "ce-source is required"; return false; }

        var id = getHeader("ce-id");
        if (string.IsNullOrWhiteSpace(id))
        { error = "ce-id is required"; return false; }

        // In binary mode the HTTP Content-Type carries datacontenttype (per
        // CloudEvents HTTP binding §3.1.4). We only accept JSON payloads.
        // Treat an absent Content-Type as application/json since the wire
        // body must be JSON anyway.
        if (!string.IsNullOrWhiteSpace(requestContentType)
            && !requestContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        { error = $"binary-mode Content-Type must be 'application/json', was '{requestContentType}'"; return false; }

        DateTimeOffset? time = null;
        var timeStr = getHeader("ce-time");
        if (!string.IsNullOrWhiteSpace(timeStr))
        {
            if (!DateTimeOffset.TryParse(timeStr, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var t))
            { error = $"ce-time: invalid RFC3339 timestamp '{timeStr}'"; return false; }
            time = t;
        }

        if (body.ToMemory().Length == 0)
        { error = "data is required"; return false; }

        NotifyCreatedV1? payload;
        try { payload = body.ToObjectFromJson<NotifyCreatedV1>(NotifyJson.Options); }
        catch (JsonException ex) { error = $"data: {ex.Message}"; return false; }
        if (payload is null) { error = "data: missing"; return false; }

        input = new IngestInput(source, time, payload);
        error = null;
        return true;
    }

    private static bool TryFromCloudEvent(
        CloudEvent ce,
        [NotNullWhen(true)] out IngestInput? input,
        [NotNullWhen(false)] out string? error)
    {
        input = null;

        if (!string.Equals(ce.Type, ExpectedType, StringComparison.Ordinal))
        { error = $"type must be '{ExpectedType}', was '{ce.Type ?? "<missing>"}'"; return false; }

        if (string.IsNullOrWhiteSpace(ce.Source))
        { error = "source is required"; return false; }

        if (string.IsNullOrWhiteSpace(ce.Id))
        { error = "id is required"; return false; }

        if (ce.DataContentType is not null
            && !ce.DataContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        { error = $"datacontenttype must be 'application/json' or omitted, was '{ce.DataContentType}'"; return false; }

        if (ce.Data is null)
        { error = "data is required"; return false; }

        NotifyCreatedV1? payload;
        try { payload = ce.Data.ToObjectFromJson<NotifyCreatedV1>(NotifyJson.Options); }
        catch (JsonException ex) { error = $"data: {ex.Message}"; return false; }
        if (payload is null) { error = "data: missing"; return false; }

        input = new IngestInput(ce.Source, ce.Time, payload);
        error = null;
        return true;
    }
}
