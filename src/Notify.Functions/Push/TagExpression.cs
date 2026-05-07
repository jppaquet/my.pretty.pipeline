namespace Notify.Functions.Push;

// Build the Notification Hubs tag expression that selects which devices
// receive a given notification. Default policy: deliver to anyone subscribed
// to the message's source AND anyone subscribed to "global" (the firehose).
//
// Per-message tag list extends this — every entry becomes its own clause:
//   source:<src> || global || tag1 || tag2 …
//
// Tag names are not validated here — DeviceApi's validator already accepts a
// permissive set, and NH itself enforces format on the tag-expression side.
public static class TagExpression
{
    public static string For(string source, IReadOnlyList<string>? extraTags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var clauses = new List<string>(2 + (extraTags?.Count ?? 0))
        {
            $"source:{source}",
            "global",
        };
        if (extraTags is not null)
        {
            foreach (var tag in extraTags)
            {
                if (!string.IsNullOrWhiteSpace(tag)) clauses.Add(tag);
            }
        }
        return string.Join(" || ", clauses);
    }
}
