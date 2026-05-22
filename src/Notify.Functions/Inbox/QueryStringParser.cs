namespace Notify.Functions.Inbox;

// Percent-only query-string parser.
//
// `System.Web.HttpUtility.ParseQueryString` is the BCL's go-to for this, but
// it follows the form-encoded convention where `+` decodes to space — which
// silently mangles Cosmos continuation tokens (they're base64-ish and
// contain `+` natively). The same goes for any producer-named partition
// key that happens to contain `+`.
//
// This parser sticks to RFC 3986 percent-decoding via `Uri.UnescapeDataString`,
// which leaves `+` alone. Use it everywhere query-string values are
// round-tripped opaquely (continuation tokens, ids, names).
public static class QueryStringParser
{
    public static IReadOnlyDictionary<string, string> Parse(string? query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return result;

        if (query[0] == '?') query = query[1..];

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            string key, value;
            if (idx < 0)
            {
                key = pair;
                value = string.Empty;
            }
            else
            {
                key = pair[..idx];
                value = pair[(idx + 1)..];
            }
            // Strict percent-decoding — `+` stays `+`, not space.
            result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value);
        }
        return result;
    }
}
