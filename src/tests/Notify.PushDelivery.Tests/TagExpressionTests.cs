using Notify.Functions.Push;

namespace Notify.Functions.Push.Tests;

public class TagExpressionTests
{
    [Fact]
    public void Default_includes_source_and_global()
    {
        Assert.Equal("source:home-pipeline || global", TagExpression.For("home-pipeline"));
    }

    [Fact]
    public void Extra_tags_namespaced_with_source_prefix()
    {
        Assert.Equal(
            "source:home-pipeline || global || tag:home-pipeline:urgent || tag:home-pipeline:backup",
            TagExpression.For("home-pipeline", new[] { "urgent", "backup" }));
    }

    [Fact]
    public void Empty_extra_tag_skipped()
    {
        Assert.Equal(
            "source:home-pipeline || global || tag:home-pipeline:keep",
            TagExpression.For("home-pipeline", new[] { "", "  ", "keep" }));
    }

    [Fact]
    public void Producer_cannot_target_another_sources_subscribers()
    {
        // A producer authenticated for `acme` submits a tag that would, without
        // namespacing, match installations subscribed to `source:rival`.
        // After namespacing, the clause becomes `tag:acme:source:rival` — which
        // matches nothing in the rival's subscription set.
        var expr = TagExpression.For("acme", new[] { "source:rival" });
        Assert.Contains("tag:acme:source:rival", expr);
        Assert.DoesNotContain(" || source:rival", expr);
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
