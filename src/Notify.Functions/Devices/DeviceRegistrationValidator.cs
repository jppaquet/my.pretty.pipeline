using Notify.Shared.Validation;

namespace Notify.Functions.Devices;

// Lightweight validator — the heavy contract validation lives in Notify.Shared
// (NotifyCreatedV1Validator). This keeps the DeviceApi self-contained until
// the iOS Codable side wants to share validation in Phase 3.
public static class DeviceRegistrationValidator
{
    public const int MaxTagCount = 50;
    public const int MaxTagLength = 120;
    // APNs device tokens are 32 bytes (64 hex chars) historically; newer
    // device tokens (iOS 13+) can be 100+ bytes. Cap at 256 hex chars to leave
    // room without inviting abuse.
    public const int MinTokenLength = 64;
    public const int MaxTokenLength = 256;

    public static ValidationResult Validate(DeviceRegistration r)
    {
        var failures = new List<ValidationFailure>();

        if (string.IsNullOrWhiteSpace(r.DeviceToken))
            failures.Add(new ValidationFailure(nameof(r.DeviceToken), "required"));
        else if (r.DeviceToken.Length < MinTokenLength || r.DeviceToken.Length > MaxTokenLength)
            failures.Add(new ValidationFailure(nameof(r.DeviceToken), $"length must be {MinTokenLength}..{MaxTokenLength} chars"));
        else if (!IsHex(r.DeviceToken))
            failures.Add(new ValidationFailure(nameof(r.DeviceToken), "must be hex"));

        if (string.IsNullOrWhiteSpace(r.Platform))
            failures.Add(new ValidationFailure(nameof(r.Platform), "required"));
        else if (!string.Equals(r.Platform, "apns", StringComparison.OrdinalIgnoreCase))
            failures.Add(new ValidationFailure(nameof(r.Platform), "only 'apns' is supported in v1"));

        if (r.Tags is { Count: > MaxTagCount })
            failures.Add(new ValidationFailure(nameof(r.Tags), $"at most {MaxTagCount} tags"));

        if (r.Tags is not null)
        {
            for (var i = 0; i < r.Tags.Count; i++)
            {
                var tag = r.Tags[i];
                if (string.IsNullOrWhiteSpace(tag))
                    failures.Add(new ValidationFailure($"{nameof(r.Tags)}[{i}]", "empty"));
                else if (tag.Length > MaxTagLength)
                    failures.Add(new ValidationFailure($"{nameof(r.Tags)}[{i}]", $"length must be ≤ {MaxTagLength} chars"));
            }
        }

        return new ValidationResult(failures);
    }

    private static bool IsHex(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            var ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!ok) return false;
        }
        return true;
    }
}
