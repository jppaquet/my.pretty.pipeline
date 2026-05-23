using System.Text.Json;
using Notify.Functions.Push;
using Notify.Shared;
using Notify.Shared.CloudEvents;

namespace Notify.Functions.Push.Tests;

public class ApnsPayloadTests
{
    private static NotifyCreatedV1 Sample(Action<NotifyCreatedV1Builder>? configure = null)
    {
        var b = new NotifyCreatedV1Builder();
        configure?.Invoke(b);
        return b.Build();
    }

    [Fact]
    public void Maps_priority_high_to_apns_10()
    {
        var p = ApnsPayload.From(Sample(b => b.Priority = Priority.High));
        Assert.Equal(10, p.ApnsPriority);
    }

    [Theory]
    [InlineData(Priority.Low)]
    [InlineData(Priority.Normal)]
    public void Maps_priority_low_and_normal_to_apns_5(Priority priority)
    {
        var p = ApnsPayload.From(Sample(b => b.Priority = priority));
        Assert.Equal(5, p.ApnsPriority);
    }

    [Fact]
    public void Builds_aps_alert_with_title_and_body()
    {
        var p = ApnsPayload.From(Sample(b =>
        {
            b.Title = "Backup failed";
            b.Body = "rsync exited 12 on host pi-01";
        }));

        using var doc = JsonDocument.Parse(p.Json);
        var alert = doc.RootElement.GetProperty("aps").GetProperty("alert");
        Assert.Equal("Backup failed", alert.GetProperty("title").GetString());
        Assert.Equal("rsync exited 12 on host pi-01", alert.GetProperty("body").GetString());
    }

    [Fact]
    public void Includes_top_level_id_source_type()
    {
        var p = ApnsPayload.From(Sample(b =>
        {
            b.Id = "abc-123";
            b.Source = "home-pipeline";
            b.Type = "warning";
        }));

        using var doc = JsonDocument.Parse(p.Json);
        Assert.Equal("abc-123", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("home-pipeline", doc.RootElement.GetProperty("source").GetString());
        Assert.Equal("warning", doc.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void Omits_deeplink_and_metadata_when_unset()
    {
        var p = ApnsPayload.From(Sample());
        using var doc = JsonDocument.Parse(p.Json);
        Assert.False(doc.RootElement.TryGetProperty("deeplink", out _));
        Assert.False(doc.RootElement.TryGetProperty("metadata", out _));
    }

    [Fact]
    public void Omits_metadata_even_when_producer_set_it()
    {
        // Validator allows up to 32 KB of metadata (#160) so the inbox
        // detail view can carry a long-form digest. That can't fit in
        // APNs' 4 KB envelope limit, and the iOS app reads metadata
        // from the inbox API anyway — keep it out of the push payload
        // unconditionally. Regression guard for the
        // `BadRequestException: Notification payload is too large` we
        // hit when ingesting Google Alerts digests.
        var fullBody = new string('x', 8 * 1024);
        var p = ApnsPayload.From(Sample(b =>
        {
            b.Metadata = new Dictionary<string, JsonElement>
            {
                ["fullBody"] = JsonDocument.Parse($"\"{fullBody}\"").RootElement,
            };
        }));

        using var doc = JsonDocument.Parse(p.Json);
        Assert.False(doc.RootElement.TryGetProperty("metadata", out _));
        Assert.InRange(p.Json.Length, 0, 4 * 1024);
    }

    [Fact]
    public void Includes_deeplink_when_set()
    {
        var p = ApnsPayload.From(Sample(b => b.Deeplink = "notify://inbox/abc-123"));
        using var doc = JsonDocument.Parse(p.Json);
        Assert.Equal("notify://inbox/abc-123", doc.RootElement.GetProperty("deeplink").GetString());
    }

    [Fact]
    public void Sound_defaults_to_default()
    {
        var p = ApnsPayload.From(Sample());
        using var doc = JsonDocument.Parse(p.Json);
        Assert.Equal("default", doc.RootElement.GetProperty("aps").GetProperty("sound").GetString());
    }

    private sealed class NotifyCreatedV1Builder
    {
        public string Source { get; set; } = "home-pipeline";
        public string Title { get; set; } = "title";
        public string Body { get; set; } = "body";
        public string Type { get; set; } = "info";
        public Priority Priority { get; set; } = Priority.Normal;
        public string? Deeplink { get; set; }
        public string Id { get; set; } = "id-1";
        public IReadOnlyDictionary<string, JsonElement>? Metadata { get; set; }

        public NotifyCreatedV1 Build() => new()
        {
            Source = Source,
            Title = Title,
            Body = Body,
            Type = Type,
            Priority = Priority,
            Deeplink = Deeplink,
            Id = Id,
            Metadata = Metadata,
        };
    }
}
