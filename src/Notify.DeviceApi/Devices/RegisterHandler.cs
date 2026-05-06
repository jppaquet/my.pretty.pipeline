using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.NotificationHubs;
using Notify.Shared.Json;
using Notify.Shared.Validation;

namespace Notify.DeviceApi.Devices;

// Pure registration logic; the Function class is a thin HTTP shim around it.
// NH calls go through INotificationHub so unit tests don't need a real hub.
public sealed class RegisterHandler
{
    private readonly INotificationHub _hub;

    public RegisterHandler(INotificationHub hub) => _hub = hub;

    public async Task<RegisterResult> HandleAsync(Stream body, long? contentLength, CancellationToken ct = default)
    {
        if (contentLength is > DeviceApiOptions.MaxRequestBodyBytes)
            return new RegisterResult.PayloadTooLarge(DeviceApiOptions.MaxRequestBodyBytes);

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
        var installation = new Installation
        {
            InstallationId = installationId,
            Platform = NotificationPlatform.Apns,
            PushChannel = input.DeviceToken,
            Tags = input.Tags is null ? null : new List<string>(input.Tags),
        };

        await _hub.UpsertInstallationAsync(installation, ct);
        return new RegisterResult.Accepted(installationId);
    }

    // Deterministic installation id keyed on the device token so re-registration
    // is idempotent (CreateOrUpdate semantics on NH).
    public static string InstallationIdFor(string deviceToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(deviceToken));
        return Convert.ToHexStringLower(hash);
    }
}
