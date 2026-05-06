using Notify.PushDelivery.Push;

namespace Notify.PushDelivery.Tests;

public class TagExpressionTests
{
    [Fact]
    public void Default_includes_source_and_global()
    {
        Assert.Equal("source:home-pipeline || global", TagExpression.For("home-pipeline"));
    }

    [Fact]
    public void Extra_tags_appended_after_global()
    {
        Assert.Equal(
            "source:home-pipeline || global || urgent || backup",
            TagExpression.For("home-pipeline", new[] { "urgent", "backup" }));
    }

    [Fact]
    public void Empty_extra_tag_skipped()
    {
        Assert.Equal(
            "source:home-pipeline || global || keep",
            TagExpression.For("home-pipeline", new[] { "", "  ", "keep" }));
    }

    [Fact]
    public void Null_extra_tags_treated_as_default()
    {
        Assert.Equal("source:home-pipeline || global", TagExpression.For("home-pipeline", null));
    }

    [Fact]
    public void Empty_source_throws()
    {
        Assert.Throws<ArgumentException>(() => TagExpression.For(""));
    }
}
