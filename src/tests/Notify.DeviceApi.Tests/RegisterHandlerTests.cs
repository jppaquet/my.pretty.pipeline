using System.Text;
using System.Text.Json;
using Microsoft.Azure.NotificationHubs;
using Notify.DeviceApi;
using Notify.DeviceApi.Devices;
using Notify.Shared.Json;

namespace Notify.DeviceApi.Tests;

public class RegisterHandlerTests
{
    private const string ValidToken =
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";  // 64 hex chars

    private static (RegisterHandler handler, RecordingHub hub) NewHandler()
    {
        var hub = new RecordingHub();
        return (new RegisterHandler(hub), hub);
    }

    private static Stream BodyOf(object obj)
        => new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj, NotifyJson.Options)));

    private static object ValidBody(string token = ValidToken, string platform = "apns", IReadOnlyList<string>? tags = null) => new
    {
        deviceToken = token,
        platform,
        tags,
    };

    [Fact]
    public async Task Oversized_payload_returns_413_without_parsing_body()
    {
        var (handler, _) = NewHandler();
        var result = await handler.HandleAsync(Stream.Null, contentLength: DeviceApiOptions.MaxRequestBodyBytes + 1);
        Assert.IsType<RegisterResult.PayloadTooLarge>(result);
    }

    [Fact]
    public async Task Malformed_json_returns_bad_request()
    {
        var (handler, _) = NewHandler();
        var bad = new MemoryStream(Encoding.UTF8.GetBytes("{not json"));
        var result = await handler.HandleAsync(bad, contentLength: null);
        var br = Assert.IsType<RegisterResult.BadRequest>(result);
        Assert.Contains(br.Failures, f => f.Field == "body");
    }

    [Fact]
    public async Task Missing_device_token_returns_bad_request()
    {
        var (handler, hub) = NewHandler();
        var body = new { deviceToken = "", platform = "apns" };
        var result = await handler.HandleAsync(BodyOf(body), contentLength: null);
        Assert.IsType<RegisterResult.BadRequest>(result);
        Assert.Empty(hub.Upserted);
    }

    [Fact]
    public async Task Non_hex_device_token_returns_bad_request()
    {
        var (handler, _) = NewHandler();
        // Same length as ValidToken but with a non-hex char.
        var token = new string('z', 64);
        var result = await handler.HandleAsync(BodyOf(ValidBody(token)), contentLength: null);
        var br = Assert.IsType<RegisterResult.BadRequest>(result);
        Assert.Contains(br.Failures, f => f.Field == "DeviceToken" && f.Message.Contains("hex"));
    }

    [Fact]
    public async Task Unsupported_platform_returns_bad_request()
    {
        var (handler, _) = NewHandler();
        var result = await handler.HandleAsync(BodyOf(ValidBody(platform: "fcm")), contentLength: null);
        var br = Assert.IsType<RegisterResult.BadRequest>(result);
        Assert.Contains(br.Failures, f => f.Field == "Platform");
    }

    [Fact]
    public async Task Too_many_tags_returns_bad_request()
    {
        var (handler, _) = NewHandler();
        var tags = Enumerable.Range(0, DeviceRegistrationValidator.MaxTagCount + 1).Select(i => $"t{i}").ToArray();
        var result = await handler.HandleAsync(BodyOf(ValidBody(tags: tags)), contentLength: null);
        var br = Assert.IsType<RegisterResult.BadRequest>(result);
        Assert.Contains(br.Failures, f => f.Field == "Tags");
    }

    [Fact]
    public async Task Happy_path_upserts_installation_and_returns_id()
    {
        var (handler, hub) = NewHandler();
        var tags = new[] { "source:home-pipeline", "global" };

        var result = await handler.HandleAsync(BodyOf(ValidBody(tags: tags)), contentLength: null);

        var accepted = Assert.IsType<RegisterResult.Accepted>(result);
        Assert.Equal(RegisterHandler.InstallationIdFor(ValidToken), accepted.InstallationId);

        var inst = Assert.Single(hub.Upserted);
        Assert.Equal(accepted.InstallationId, inst.InstallationId);
        Assert.Equal(NotificationPlatform.Apns, inst.Platform);
        Assert.Equal(ValidToken, inst.PushChannel);
        Assert.NotNull(inst.Tags);
        Assert.Equal(tags, inst.Tags);
    }

    [Fact]
    public async Task Same_token_produces_same_installation_id_idempotency()
    {
        var (handler, hub) = NewHandler();

        await handler.HandleAsync(BodyOf(ValidBody()), contentLength: null);
        await handler.HandleAsync(BodyOf(ValidBody()), contentLength: null);

        Assert.Equal(2, hub.Upserted.Count);
        Assert.Equal(hub.Upserted[0].InstallationId, hub.Upserted[1].InstallationId);
    }

    [Fact]
    public async Task Tokens_are_case_insensitive_but_id_is_not()
    {
        // Validator accepts both A-F and a-f; the deterministic id treats the
        // bytes literally, so callers should pick one casing and stick with it.
        var (handler, hub) = NewHandler();
        await handler.HandleAsync(BodyOf(ValidBody(ValidToken.ToUpperInvariant())), contentLength: null);
        await handler.HandleAsync(BodyOf(ValidBody(ValidToken.ToLowerInvariant())), contentLength: null);

        Assert.NotEqual(hub.Upserted[0].InstallationId, hub.Upserted[1].InstallationId);
    }
}
