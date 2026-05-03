using System.Text.Json;
using Notify.Shared.CloudEvents;
using Notify.Shared.Json;

namespace Notify.Shared.Tests;

public class CloudEventEnvelopeTests
{
    private static NotifyCreatedV1 Sample() => new()
    {
        Source = "home-pipeline",
        Title = "Backup failed",
        Body = "rsync exited 12",
        Priority = Priority.High,
    };

    [Fact]
    public void From_sets_canonical_fields()
    {
        var id = Guid.NewGuid();
        var time = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        var env = CloudEventEnvelope.From(Sample(), id, time);

        Assert.Equal("1.0", env.SpecVersion);
        Assert.Equal(id.ToString("D"), env.Id);
        Assert.Equal("urn:notify:home-pipeline", env.Source);
        Assert.Equal(CloudEventEnvelope.CurrentType, env.Type);
        Assert.Equal(time, env.Time);
        Assert.Equal("application/json", env.DataContentType);
        Assert.Equal(Sample(), env.Data);
    }

    [Fact]
    public void Json_roundtrip_preserves_envelope()
    {
        var env = CloudEventEnvelope.From(Sample(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(env, NotifyJson.Options);
        var roundtripped = JsonSerializer.Deserialize<CloudEventEnvelope>(json, NotifyJson.Options);

        Assert.NotNull(roundtripped);
        Assert.Equal(env, roundtripped);
    }

    [Fact]
    public void Json_uses_camelCase_field_names()
    {
        var env = CloudEventEnvelope.From(Sample(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(env, NotifyJson.Options);

        Assert.Contains("\"specVersion\"", json);
        Assert.Contains("\"dataContentType\"", json);
        Assert.Contains("\"data\"", json);
    }

    [Fact]
    public void Priority_serializes_as_lowercase_string()
    {
        var env = CloudEventEnvelope.From(Sample(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(env, NotifyJson.Options);

        Assert.Contains("\"priority\":\"high\"", json);
    }

    [Fact]
    public void Source_urn_is_built_from_data_source()
    {
        var data = Sample() with { Source = "ci-bot" };
        var env = CloudEventEnvelope.From(data, Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Equal("urn:notify:ci-bot", env.Source);
    }
}
