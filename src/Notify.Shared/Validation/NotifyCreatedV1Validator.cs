using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Notify.Shared.CloudEvents;
using Notify.Shared.Json;

namespace Notify.Shared.Validation;

public static class NotifyCreatedV1Validator
{
    public const int TitleMaxChars = 120;
    public const int BodyMaxChars = 2000;
    public const int MetadataMaxBytes = 4 * 1024;
    public const int TagsMaxCount = 10;
    public const int TagMaxChars = 64;

    // Producer tags become NH tag-expression clauses (see Push/TagExpression).
    // Restricting to [A-Za-z0-9._-] keeps every NH boolean operator (| & ! ( ) :
    // whitespace) out of the clause, preventing a producer from forging
    // additional clauses or matching platform-owned clauses like `source:<x>`.
    private static readonly Regex TagPattern = new(@"\A[A-Za-z0-9._-]+\z", RegexOptions.Compiled);

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

        if (input.Tags is { Count: > 0 } tags)
        {
            if (tags.Count > TagsMaxCount)
                failures.Add(new("tags", $"max {TagsMaxCount} tags, was {tags.Count}"));

            for (var i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                if (string.IsNullOrWhiteSpace(tag))
                    failures.Add(new($"tags[{i}]", "must be non-empty"));
                else if (tag.Length > TagMaxChars)
                    failures.Add(new($"tags[{i}]", $"max {TagMaxChars} chars"));
                else if (!TagPattern.IsMatch(tag))
                    failures.Add(new($"tags[{i}]", "allowed chars: A-Z a-z 0-9 . _ -"));
                else if (string.Equals(tag, "global", StringComparison.OrdinalIgnoreCase))
                    failures.Add(new($"tags[{i}]", "reserved"));
            }
        }

        return failures.Count == 0 ? ValidationResult.Valid : new ValidationResult(failures);
    }
}
