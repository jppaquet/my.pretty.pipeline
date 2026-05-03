using System.Text;
using System.Text.Json;
using Notify.Shared.CloudEvents;
using Notify.Shared.Json;

namespace Notify.Shared.Validation;

public static class NotifyCreatedV1Validator
{
    public const int TitleMaxChars = 120;
    public const int BodyMaxChars = 2000;
    public const int MetadataMaxBytes = 4 * 1024;

    public static ValidationResult Validate(NotifyCreatedV1 input)
    {
        var failures = new List<ValidationFailure>();

        if (string.IsNullOrWhiteSpace(input.Source))
            failures.Add(new("source", "required"));

        if (string.IsNullOrWhiteSpace(input.Title))
            failures.Add(new("title", "required"));
        else if (input.Title.Length > TitleMaxChars)
            failures.Add(new("title", $"max {TitleMaxChars} chars"));

        if (string.IsNullOrWhiteSpace(input.Body))
            failures.Add(new("body", "required"));
        else if (input.Body.Length > BodyMaxChars)
            failures.Add(new("body", $"max {BodyMaxChars} chars"));

        if (input.Metadata is not null)
        {
            var serialized = JsonSerializer.Serialize(input.Metadata, NotifyJson.Options);
            var bytes = Encoding.UTF8.GetByteCount(serialized);
            if (bytes > MetadataMaxBytes)
                failures.Add(new("metadata", $"max {MetadataMaxBytes} bytes serialized, was {bytes}"));
        }

        return failures.Count == 0 ? ValidationResult.Valid : new ValidationResult(failures);
    }
}
