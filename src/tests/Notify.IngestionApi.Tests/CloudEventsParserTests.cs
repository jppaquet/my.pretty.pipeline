using System.Text;
using Notify.Functions.Ingestion;

namespace Notify.Functions.Ingestion.Tests;

public class CloudEventsParserTests
{
    private static BinaryData Json(string s) => new(Encoding.UTF8.GetBytes(s));

    private const string ValidStructured = """
        {"specversion":"1.0","type":"notify.created.v1","source":"watchtower",
         "id":"abc-1","time":"2026-05-18T12:00:00Z",
         "datacontenttype":"application/json",
         "data":{"title":"Hello","body":"world"}}
        """;

    // ── structured single ────────────────────────────────────────────

    [Fact]
    public void Structured_happy_path_parses()
    {
        Assert.True(CloudEventsParser.TryParseStructured(Json(ValidStructured), out var input, out var err));
        Assert.Null(err);
        Assert.Equal("watchtower", input.Source);
        Assert.NotNull(input.Time);
        Assert.Equal(new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero), input.Time);
        Assert.Equal("Hello", input.Data.Title);
        Assert.Equal("world", input.Data.Body);
    }

    [Fact]
    public void Structured_wrong_type_rejected()
    {
        var body = ValidStructured.Replace("notify.created.v1", "some.other.type");
        Assert.False(CloudEventsParser.TryParseStructured(Json(body), out _, out var err));
        Assert.Contains("type must be", err);
    }

    [Fact]
    public void Structured_missing_id_rejected()
    {
        var body = """
            {"specversion":"1.0","type":"notify.created.v1","source":"x",
             "data":{"title":"t","body":"b"}}
            """;
        Assert.False(CloudEventsParser.TryParseStructured(Json(body), out _, out var err));
        // Azure CloudEvent.Parse throws when required `id` is missing; the
        // parser surfaces the failure as a malformed envelope error.
        Assert.NotNull(err);
    }

    [Fact]
    public void Structured_bad_datacontenttype_rejected()
    {
        var body = ValidStructured.Replace("application/json", "application/xml");
        Assert.False(CloudEventsParser.TryParseStructured(Json(body), out _, out var err));
        Assert.Contains("datacontenttype", err);
    }

    [Fact]
    public void Structured_missing_data_rejected()
    {
        var body = """
            {"specversion":"1.0","type":"notify.created.v1","source":"x","id":"i"}
            """;
        Assert.False(CloudEventsParser.TryParseStructured(Json(body), out _, out var err));
        Assert.Contains("data", err);
    }

    [Fact]
    public void Structured_malformed_json_rejected()
    {
        Assert.False(CloudEventsParser.TryParseStructured(Json("{not json"), out _, out var err));
        Assert.NotNull(err);
    }

    // ── batch ────────────────────────────────────────────────────────

    [Fact]
    public void Batch_happy_path_parses_all_events()
    {
        var body = $"[{ValidStructured},{ValidStructured.Replace("abc-1", "abc-2")}]";
        Assert.True(CloudEventsParser.TryParseBatch(Json(body), out var inputs, out var err));
        Assert.Null(err);
        Assert.Equal(2, inputs.Count);
    }

    [Fact]
    public void Batch_empty_array_rejected()
    {
        Assert.False(CloudEventsParser.TryParseBatch(Json("[]"), out _, out var err));
        Assert.NotNull(err);
    }

    [Fact]
    public void Batch_per_event_failure_namespaces_error_to_index()
    {
        var bad = ValidStructured.Replace("notify.created.v1", "wrong.type");
        var body = $"[{ValidStructured},{bad}]";
        Assert.False(CloudEventsParser.TryParseBatch(Json(body), out _, out var err));
        Assert.Contains("events[1]", err);
    }

    // ── binary single ────────────────────────────────────────────────

    private static Func<string, string?> HeadersFrom(Dictionary<string, string> map)
        => name => map.TryGetValue(name, out var v) ? v : null;

    [Fact]
    public void Binary_happy_path_parses()
    {
        var headers = new Dictionary<string, string>
        {
            ["ce-specversion"] = "1.0",
            ["ce-type"] = "notify.created.v1",
            ["ce-source"] = "watchtower",
            ["ce-id"] = "abc-1",
            ["ce-time"] = "2026-05-18T12:00:00Z",
        };
        var body = Json("""{"title":"Hello","body":"world"}""");

        Assert.True(CloudEventsParser.TryParseBinary(HeadersFrom(headers), "application/json", body, out var input, out var err));
        Assert.Null(err);
        Assert.Equal("watchtower", input.Source);
        Assert.Equal(new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero), input.Time);
        Assert.Equal("Hello", input.Data.Title);
    }

    [Fact]
    public void Binary_missing_specversion_rejected()
    {
        var headers = new Dictionary<string, string> { ["ce-type"] = "notify.created.v1", ["ce-source"] = "x", ["ce-id"] = "i" };
        Assert.False(CloudEventsParser.TryParseBinary(HeadersFrom(headers), "application/json", Json("""{"title":"t","body":"b"}"""), out _, out var err));
        Assert.Contains("ce-specversion", err);
    }

    [Fact]
    public void Binary_wrong_specversion_rejected()
    {
        var headers = new Dictionary<string, string>
        {
            ["ce-specversion"] = "0.3",
            ["ce-type"] = "notify.created.v1",
            ["ce-source"] = "x",
            ["ce-id"] = "i",
        };
        Assert.False(CloudEventsParser.TryParseBinary(HeadersFrom(headers), "application/json", Json("""{"title":"t","body":"b"}"""), out _, out var err));
        Assert.Contains("ce-specversion", err);
    }

    [Fact]
    public void Binary_wrong_type_rejected()
    {
        var headers = new Dictionary<string, string>
        {
            ["ce-specversion"] = "1.0",
            ["ce-type"] = "some.other.type",
            ["ce-source"] = "x",
            ["ce-id"] = "i",
        };
        Assert.False(CloudEventsParser.TryParseBinary(HeadersFrom(headers), "application/json", Json("""{"title":"t","body":"b"}"""), out _, out var err));
        Assert.Contains("ce-type", err);
    }

    [Fact]
    public void Binary_non_json_content_type_rejected()
    {
        var headers = new Dictionary<string, string>
        {
            ["ce-specversion"] = "1.0",
            ["ce-type"] = "notify.created.v1",
            ["ce-source"] = "x",
            ["ce-id"] = "i",
        };
        Assert.False(CloudEventsParser.TryParseBinary(HeadersFrom(headers), "application/xml", Json("<x/>"), out _, out var err));
        Assert.Contains("application/json", err);
    }

    [Fact]
    public void Binary_empty_body_rejected()
    {
        var headers = new Dictionary<string, string>
        {
            ["ce-specversion"] = "1.0",
            ["ce-type"] = "notify.created.v1",
            ["ce-source"] = "x",
            ["ce-id"] = "i",
        };
        Assert.False(CloudEventsParser.TryParseBinary(HeadersFrom(headers), "application/json", new BinaryData(Array.Empty<byte>()), out _, out var err));
        Assert.Contains("data", err);
    }
}
