namespace Notify.Functions.Auth;

// Identity-layer config. The audience claim on Sign-in-with-Apple identity
// tokens is the iOS app's bundle identifier — forks pick their own and set
// `AppleAudience` accordingly. The issuer is constant (Apple) and not
// configurable.
//
// The Cosmos pointers below feed CosmosAllowlistRepository: every
// authenticated request reads `allowedUsers/<sub>` to gate access. Empty
// container name = allowlist disabled (every valid SiwA JWT is accepted),
// which preserves pre-allowlist behavior for forks that haven't deployed
// the bicep changes yet.
public sealed record AuthOptions
{
    public string AppleAudience { get; init; } = "";
    public string CosmosDatabase { get; init; } = "";
    public string CosmosAllowedUsersContainer { get; init; } = "";
}
