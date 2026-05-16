using System.Text;
using System.Text.Json;
using Microsoft.Azure.NotificationHubs;
using Notify.Functions.Devices;
using Notify.Shared.Json;

namespace Notify.Functions.Devices.Tests;

public class RegisterHandlerTests
{
    private const string ValidToken =
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";  // 64 hex chars

    private const string UserSub = "001234.abcdef";

    private static (RegisterHandler handler, RecordingHub hub, InMemoryDeviceStore store) NewHandler()
    {
        var hub = new RecordingHub();
        var store = new InMemoryDeviceStore();
        return (new RegisterHandler(hub, store), hub, store);
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
        var (handler, _, _) = NewHandler();
        var result = await handler.HandleAsync(UserSub, Stream.Null, contentLength: DevicesOptions.MaxRequestBodyBytes + 1);
        Assert.IsType<RegisterResult.PayloadTooLarge>(result);
    }

    [Fact]
    public async Task Malformed_json_returns_bad_request()
    {
        var (handler, _, _) = NewHandler();
        var bad = new MemoryStream(Encoding.UTF8.GetBytes("{not json"));
        var result = await handler.HandleAsync(UserSub, bad, contentLength: null);
        var br = Assert.IsType<RegisterResult.BadRequest>(result);
        Assert.Contains(br.Failures, f => f.Field == "body");
    }

    [Fact]
    public async Task Missing_device_token_returns_bad_request()
    {
        var (handler, hub, store) = NewHandler();
        var body = new { deviceToken = "", platform = "apns" };
        var result = await handler.HandleAsync(UserSub, BodyOf(body), contentLength: null);
        Assert.IsType<RegisterResult.BadRequest>(result);
        Assert.Empty(hub.Upserted);
        Assert.Empty(store.Upserted);
    }

    [Fact]
    public async Task Non_hex_device_token_returns_bad_request()
    {
        var (handler, _, _) = NewHandler();
        var token = new string('z', 64);
        var result = await handler.HandleAsync(UserSub, BodyOf(ValidBody(token)), contentLength: null);
        var br = Assert.IsType<RegisterResult.BadRequest>(result);
        Assert.Contains(br.Failures, f => f.Field == "DeviceToken" && f.Message.Contains("hex"));
    }

    [Fact]
    public async Task Unsupported_platform_returns_bad_request()
    {
        var (handler, _, _) = NewHandler();
        var result = await handler.HandleAsync(UserSub, BodyOf(ValidBody(platform: "fcm")), contentLength: null);
        var br = Assert.IsType<RegisterResult.BadRequest>(result);
        Assert.Contains(br.Failures, f => f.Field == "Platform");
    }

    [Fact]
    public async Task Too_many_tags_returns_bad_request()
    {
        var (handler, _, _) = NewHandler();
        var tags = Enumerable.Range(0, DeviceRegistrationValidator.MaxTagCount + 1).Select(i => $"t{i}").ToArray();
        var result = await handler.HandleAsync(UserSub, BodyOf(ValidBody(tags: tags)), contentLength: null);
        var br = Assert.IsType<RegisterResult.BadRequest>(result);
        Assert.Contains(br.Failures, f => f.Field == "Tags");
    }

    [Fact]
    public async Task Happy_path_upserts_installation_and_device_doc()
    {
        var (handler, hub, store) = NewHandler();

        var result = await handler.HandleAsync(UserSub, BodyOf(ValidBody()), contentLength: null);

        var accepted = Assert.IsType<RegisterResult.Accepted>(result);
        Assert.Equal(RegisterHandler.InstallationIdFor(ValidToken), accepted.InstallationId);

        var inst = Assert.Single(hub.Upserted);
        Assert.Equal(accepted.InstallationId, inst.InstallationId);
        Assert.Equal(NotificationPlatform.Apns, inst.Platform);
        Assert.Equal(ValidToken, inst.PushChannel);

        var doc = Assert.Single(store.Upserted);
        Assert.Equal(accepted.InstallationId, doc.DeviceId);
        Assert.Equal(UserSub, doc.UserId);
        Assert.Equal(ValidToken, doc.ApnsToken);
    }

    [Fact]
    public async Task Tags_are_server_derived_not_client_supplied()
    {
        // Client tries to subscribe to another user's audience.
        var (handler, hub, store) = NewHandler();
        var malicious = new[] { "user:001234.victim", "source:rival" };

        await handler.HandleAsync(UserSub, BodyOf(ValidBody(tags: malicious)), contentLength: null);

        var expected = RegisterHandler.TagsFor(UserSub);
        Assert.Equal(expected, hub.Upserted.Single().Tags);
        Assert.Equal(expected, store.Upserted.Single().Tags);
        Assert.DoesNotContain("user:001234.victim", store.Upserted.Single().Tags);
        Assert.DoesNotContain("source:rival", store.Upserted.Single().Tags);
    }

    [Fact]
    public async Task Same_token_produces_same_installation_id_idempotency()
    {
        var (handler, hub, _) = NewHandler();

        await handler.HandleAsync(UserSub, BodyOf(ValidBody()), contentLength: null);
        await handler.HandleAsync(UserSub, BodyOf(ValidBody()), contentLength: null);

        Assert.Equal(2, hub.Upserted.Count);
        Assert.Equal(hub.Upserted[0].InstallationId, hub.Upserted[1].InstallationId);
    }

    [Fact]
    public async Task Tokens_are_case_insensitive_but_id_is_not()
    {
        var (handler, hub, _) = NewHandler();
        await handler.HandleAsync(UserSub, BodyOf(ValidBody(ValidToken.ToUpperInvariant())), contentLength: null);
        await handler.HandleAsync(UserSub, BodyOf(ValidBody(ValidToken.ToLowerInvariant())), contentLength: null);

        Assert.NotEqual(hub.Upserted[0].InstallationId, hub.Upserted[1].InstallationId);
    }

    [Fact]
    public async Task Empty_user_id_throws()
    {
        var (handler, _, _) = NewHandler();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.HandleAsync("", BodyOf(ValidBody()), contentLength: null));
    }
}
