namespace Notify.Functions.Push;

// Build the Notification Hubs tag expression that selects which devices
// receive a given notification. Default policy: deliver to anyone subscribed
// to the message's source AND anyone subscribed to "global" (the firehose).
//
// Per-message tag list extends this — every producer-supplied tag becomes
// `tag:<source>:<value>`:
//   source:<src> || global || tag:<src>:t1 || tag:<src>:t2 …
//
// The `tag:<source>:` prefix is the security boundary that prevents a
// producer authenticated for project A from forging a clause that matches
// project B's installations. NotifyCreatedV1Validator already restricts the
// tag charset so a producer can't break out of the clause syntactically; the
// namespace ensures they can't *semantically* match anything outside their
// own project either.
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
                if (!string.IsNullOrWhiteSpace(tag)) clauses.Add($"tag:{source}:{tag}");
            }
        }
        return string.Join(" || ", clauses);
    }
}
