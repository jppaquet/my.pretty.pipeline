namespace Notify.Functions.Auth;

// Row in the `allowedUsers` Cosmos container. Created on first sign-in with
// `Approved=false`; the admin flips `Approved=true` in Cosmos Data Explorer
// to enroll the tester. Partition key = id = Apple `sub`.
//
// `FirstSeenAt` is set once on insert and never updated — useful for sorting
// the pending list in the portal. `ApprovedAt` is unused at the code layer;
// it's there so an admin can stamp it manually for audit purposes if they
// care.
public sealed record AllowedUserDocument
{
    public required string Id { get; init; }
    public required string Sub { get; init; }
    public required bool Approved { get; init; }
    public required DateTimeOffset FirstSeenAt { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
}
