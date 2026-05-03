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
}
