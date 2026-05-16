using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.NotificationHubs;
using Notify.Shared.Cosmos;
using Notify.Shared.Json;
using Notify.Shared.Validation;

namespace Notify.Functions.Devices;

// Pure registration logic; the Function class is a thin HTTP shim around it.
// NH calls go through INotificationHub and Cosmos goes through IDeviceStore
// so unit tests don't need real backends.
//
// Tags are server-derived from the authenticated `userId` and a small static
// allow-list — the client's `tags` field is intentionally ignored so a token
// holder can't subscribe their device to another user's audience (closes
// the H3 finding from the security audit).
public sealed class RegisterHandler
{
    private readonly INotificationHub _hub;
    private readonly IDeviceStore _store;
    private readonly TimeProvider _clock;

    public RegisterHandler(INotificationHub hub, IDeviceStore store, TimeProvider? clock = null)
    {
        _hub = hub;
        _store = store;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<RegisterResult> HandleAsync(string userId, Stream body, long? contentLength, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (contentLength is > DevicesOptions.MaxRequestBodyBytes)
            return new RegisterResult.PayloadTooLarge(DevicesOptions.MaxRequestBodyBytes);

        DeviceRegistration? input;
        try
        {
            input = await JsonSerializer.DeserializeAsync<DeviceRegistration>(body, NotifyJson.Options, ct);
        }
        catch (JsonException ex)
        {
            return new RegisterResult.BadRequest(new[] { new ValidationFailure("body", ex.Message) });
        }

        if (input is null)
            return new RegisterResult.BadRequest(new[] { new ValidationFailure("body", "missing") });

        var validation = DeviceRegistrationValidator.Validate(input);
        if (!validation.IsValid)
            return new RegisterResult.BadRequest(validation.Failures);

        var installationId = InstallationIdFor(input.DeviceToken);
        var tags = TagsFor(userId);

        var installation = new Installation
        {
            InstallationId = installationId,
            Platform = NotificationPlatform.Apns,
            PushChannel = input.DeviceToken,
            Tags = new List<string>(tags),
        };

        await _hub.UpsertInstallationAsync(installation, ct);

        var deviceDoc = new DeviceDocument
        {
            Id = installationId,
            DeviceId = installationId,
            UserId = userId,
            ApnsToken = input.DeviceToken,
            Tags = tags,
            UpdatedAt = _clock.GetUtcNow(),
        };
        await _store.UpsertAsync(deviceDoc, ct);

        return new RegisterResult.Accepted(installationId);
    }

    // Deterministic installation id keyed on the device token so re-registration
    // is idempotent (CreateOrUpdate semantics on NH).
    public static string InstallationIdFor(string deviceToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(deviceToken));
        return Convert.ToHexStringLower(hash);
    }

    // Server-derived tag set bound to the authenticated user. `global` keeps
    // backward compat with the platform-wide firehose clause emitted by
    // Push/TagExpression. `user:<sub>` is the per-user routing handle used
    // when push targeting becomes user-specific (PR-D+).
    public static IReadOnlyList<string> TagsFor(string userId)
        => new[] { "global", $"user:{userId}" };
}
