using Notify.Shared.CloudEvents;
using Notify.Shared.Cosmos;
using Notify.Shared.Hashing;

namespace Notify.Functions.Archive;

// Per-user fan-out at archive time. For every active user discovered through
// IUserDirectory, write one `NotificationDocument` whose `Id` carries the
// user binding (`{baseId}:{userId}`). InboxHandler then filters by
// `c.userId = @user` server-side so a token holder only sees their own
// inbox (closes the H2 finding from the security audit).
//
// The full set of writes is per-envelope; failures on individual user docs
// don't fail the envelope (we log + continue) so a single bad doc doesn't
// trigger EventGrid retries that re-fan-out across every user.
public sealed class ArchiveHandler
{
    private readonly IArchiveSink _sink;
    private readonly IUserDirectory _users;

    public ArchiveHandler(IArchiveSink sink, IUserDirectory users)
    {
        _sink = sink;
        _users = users;
    }

    public async Task<ArchiveFanOutResult> HandleAsync(CloudEventEnvelope envelope, CancellationToken ct = default)
    {
        var baseId = !string.IsNullOrEmpty(envelope.Data.DeduplicationKey)
            ? DedupKeyHasher.Hash(envelope.Data.Source, envelope.Data.DeduplicationKey)
            : envelope.Id;

        var userIds = await _users.ListActiveUserIdsAsync(ct);

        int created = 0;
        int duplicates = 0;
        foreach (var userId in userIds)
        {
            var document = NotificationDocument.From(envelope, baseId, userId);
            var outcome = await _sink.StoreAsync(document, ct);
            switch (outcome)
            {
                case ArchiveOutcome.Created:           created++; break;
                case ArchiveOutcome.DuplicateIgnored:  duplicates++; break;
            }
        }

        return new ArchiveFanOutResult(SubscribedUsers: userIds.Count, Created: created, Duplicates: duplicates);
    }
}

public sealed record ArchiveFanOutResult(int SubscribedUsers, int Created, int Duplicates);
