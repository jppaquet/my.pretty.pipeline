using Notify.Shared.CloudEvents;
using Notify.Shared.Validation;

namespace Notify.Shared.Tests;

public class NotifyCreatedV1ValidatorTests
{
    private static NotifyCreatedV1 Valid(Action<NotifyCreatedV1>? mutate = null)
    {
        var v = new NotifyCreatedV1
        {
            Source = "home-pipeline",
            Title = "Backup failed",
            Body = "rsync exited 12 on host pi-01",
        };
        mutate?.Invoke(v);
        return v;
    }

    [Fact]
    public void Valid_input_passes()
    {
        Assert.True(NotifyCreatedV1Validator.Validate(Valid()).IsValid);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("title")]
    [InlineData("body")]
    public void Required_field_missing_fails(string field)
    {
        var input = field switch
        {
            "source" => new NotifyCreatedV1 { Source = "", Title = "t", Body = "b" },
            "title"  => new NotifyCreatedV1 { Source = "s", Title = "", Body = "b" },
            "body"   => new NotifyCreatedV1 { Source = "s", Title = "t", Body = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        var result = NotifyCreatedV1Validator.Validate(input);
        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, f => f.Field == field);
    }

    [Fact]
    public void Title_too_long_fails()
    {
        var input = new NotifyCreatedV1
        {
            Source = "s",
            Title = new string('x', NotifyCreatedV1Validator.TitleMaxChars + 1),
            Body = "b",
        };
        var result = NotifyCreatedV1Validator.Validate(input);
        Assert.Contains(result.Failures, f => f.Field == "title");
    }

    [Fact]
    public void Body_too_long_fails()
    {
        var input = new NotifyCreatedV1
        {
            Source = "s",
            Title = "t",
            Body = new string('x', NotifyCreatedV1Validator.BodyMaxChars + 1),
        };
        var result = NotifyCreatedV1Validator.Validate(input);
        Assert.Contains(result.Failures, f => f.Field == "body");
    }

    [Fact]
    public void Metadata_over_4kb_fails()
    {
        var big = new string('x', NotifyCreatedV1Validator.MetadataMaxBytes);
        var input = new NotifyCreatedV1
        {
            Source = "s",
            Title = "t",
            Body = "b",
            Metadata = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["k"] = System.Text.Json.JsonDocument.Parse($"\"{big}\"").RootElement,
            },
        };
        var result = NotifyCreatedV1Validator.Validate(input);
        Assert.Contains(result.Failures, f => f.Field == "metadata");
    }

    [Fact]
    public void Title_at_exact_limit_passes()
    {
        var input = new NotifyCreatedV1
        {
            Source = "s",
            Title = new string('x', NotifyCreatedV1Validator.TitleMaxChars),
            Body = "b",
        };
        Assert.True(NotifyCreatedV1Validator.Validate(input).IsValid);
    }

    [Theory]
    [InlineData("pi-01")]
    [InlineData("ABC.def_123")]
    [InlineData("a")]
    public void Tag_with_allowed_charset_passes(string tag)
    {
        var input = Valid() with { Tags = new[] { tag } };
        Assert.True(NotifyCreatedV1Validator.Validate(input).IsValid);
    }

    [Theory]
    [InlineData("source:rival")]      // colon — would forge an NH clause
    [InlineData("a || b")]             // boolean operator
    [InlineData("a && b")]
    [InlineData("!negate")]
    [InlineData("(grouped)")]
    [InlineData("space tag")]
    [InlineData("tab\ttag")]
    public void Tag_with_operator_chars_fails(string tag)
    {
        var input = Valid() with { Tags = new[] { tag } };
        var result = NotifyCreatedV1Validator.Validate(input);
        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, f => f.Field.StartsWith("tags["));
    }

    [Theory]
    [InlineData("global")]
    [InlineData("Global")]
    [InlineData("GLOBAL")]
    public void Tag_reserved_name_fails(string tag)
    {
        var input = Valid() with { Tags = new[] { tag } };
        var result = NotifyCreatedV1Validator.Validate(input);
        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, f => f.Field.StartsWith("tags["));
    }

    [Fact]
    public void Tag_empty_or_whitespace_fails()
    {
        var input = Valid() with { Tags = new[] { "ok", "", "  " } };
        var result = NotifyCreatedV1Validator.Validate(input);
        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, f => f.Field == "tags[1]");
        Assert.Contains(result.Failures, f => f.Field == "tags[2]");
    }

    [Fact]
    public void Tag_over_max_chars_fails()
    {
        var input = Valid() with { Tags = new[] { new string('x', NotifyCreatedV1Validator.TagMaxChars + 1) } };
        var result = NotifyCreatedV1Validator.Validate(input);
        Assert.Contains(result.Failures, f => f.Field == "tags[0]");
    }

    [Fact]
    public void Tags_over_max_count_fails()
    {
        var tags = Enumerable.Range(0, NotifyCreatedV1Validator.TagsMaxCount + 1)
                             .Select(i => $"t{i}").ToArray();
        var input = Valid() with { Tags = tags };
        var result = NotifyCreatedV1Validator.Validate(input);
        Assert.Contains(result.Failures, f => f.Field == "tags");
    }

    [Fact]
    public void Tags_null_or_empty_passes()
    {
        Assert.True(NotifyCreatedV1Validator.Validate(Valid() with { Tags = null }).IsValid);
        Assert.True(NotifyCreatedV1Validator.Validate(Valid() with { Tags = Array.Empty<string>() }).IsValid);
    }
}
