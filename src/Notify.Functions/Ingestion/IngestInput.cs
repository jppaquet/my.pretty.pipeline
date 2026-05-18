using Notify.Shared.CloudEvents;

namespace Notify.Functions.Ingestion;

// One CloudEvent's worth of parsed request, ready for auth + validation +
// publish. `Source` is the CloudEvents `source` attribute (the producer's
// project id); `Time` is `ce.time` (server fills if null); `Data` is the
// deserialized payload — its own `Source` is ignored and server-locked by the
// handler before validation.
public sealed record IngestInput(string Source, DateTimeOffset? Time, NotifyCreatedV1 Data);
