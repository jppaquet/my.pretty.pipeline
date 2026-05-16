namespace Notify.Functions.Auth;

// Backs JwtAuthMiddleware's per-sub gate. Exactly one method because the
// middleware doesn't need fine-grained verbs — every consumer is "did this
// sub get approved?" and the repository is what owns the self-registration
// side effect on a miss.
//
// Behind the interface so unit tests can fake the gate without spinning up
// the Cosmos emulator.
public interface IAllowlistRepository
{
    // Returns true iff `sub` has a row in `allowedUsers` with `approved=true`.
    // On miss, the implementation upserts a `approved=false` row so the sub
    // shows up in Cosmos Data Explorer for an admin to flip; the call still
    // returns false. Race-safe (concurrent inserts collapse).
    Task<bool> IsApprovedAsync(string sub, CancellationToken ct);
}

// Used when AuthOptions.CosmosAllowedUsersContainer is empty — preserves the
// pre-allowlist behavior (any valid SiwA JWT is accepted). Keeps the DI
// surface stable so the middleware doesn't need an "is enabled?" branch.
public sealed class AlwaysApproveAllowlistRepository : IAllowlistRepository
{
    public Task<bool> IsApprovedAsync(string sub, CancellationToken ct) => Task.FromResult(true);
}
