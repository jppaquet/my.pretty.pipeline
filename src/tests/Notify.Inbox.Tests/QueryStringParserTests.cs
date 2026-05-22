using Notify.Functions.Inbox;

namespace Notify.Functions.Inbox.Tests;

// Unit tests for the strict percent-only query-string parser. The
// motivating bug: Cosmos continuation tokens contain `+` characters
// (base64 alphabet) — `HttpUtility.ParseQueryString` follows the
// form-encoded convention where `+` decodes to space, mangling the
// token and turning the next paginated request into a Cosmos 400 →
// unhandled exception → 500 from the inbox endpoint.
public class QueryStringParserTests
{
    [Fact]
    public void Parses_basic_key_value_pairs()
    {
        var q = QueryStringParser.Parse("?limit=50&source=watchtower");
        Assert.Equal("50", q["limit"]);
        Assert.Equal("watchtower", q["source"]);
    }

    [Fact]
    public void Tolerates_leading_question_mark_being_absent()
    {
        var q = QueryStringParser.Parse("limit=10");
        Assert.Equal("10", q["limit"]);
    }

    [Fact]
    public void Empty_and_null_input_yield_empty_dictionary()
    {
        Assert.Empty(QueryStringParser.Parse(null));
        Assert.Empty(QueryStringParser.Parse(""));
        Assert.Empty(QueryStringParser.Parse("?"));
    }

    [Fact]
    public void Preserves_plus_sign_as_literal_not_space()
    {
        // The motivating bug. A Cosmos continuation token shape like
        // `{"token":"abc+def"}` percent-encodes to `%7B%22token%22%3A%22abc%2Bdef%22%7D`
        // when sent properly, but iOS URLQueryItem sometimes leaves
        // `+` raw in the query (RFC 3986 doesn't require encoding it).
        // The form-encoded BCL parser would turn that raw `+` into a
        // space. The strict parser keeps it literal.
        var q = QueryStringParser.Parse("?continuationToken=abc+def");
        Assert.Equal("abc+def", q["continuationToken"]);
    }

    [Fact]
    public void Decodes_percent_encoded_values()
    {
        var q = QueryStringParser.Parse("?source=watch%2Btower&continuationToken=%7B%22a%22%3A1%7D");
        Assert.Equal("watch+tower", q["source"]);
        Assert.Equal("{\"a\":1}", q["continuationToken"]);
    }

    [Fact]
    public void Handles_keys_without_values()
    {
        var q = QueryStringParser.Parse("?debug&source=foo");
        Assert.Equal("", q["debug"]);
        Assert.Equal("foo", q["source"]);
    }

    [Fact]
    public void Key_lookup_is_case_insensitive()
    {
        var q = QueryStringParser.Parse("?Source=Foo");
        Assert.Equal("Foo", q["source"]);
        Assert.Equal("Foo", q["SOURCE"]);
    }

    [Fact]
    public void Skips_empty_segments_from_double_ampersands()
    {
        var q = QueryStringParser.Parse("?a=1&&b=2");
        Assert.Equal("1", q["a"]);
        Assert.Equal("2", q["b"]);
        Assert.Equal(2, q.Count);
    }
}
