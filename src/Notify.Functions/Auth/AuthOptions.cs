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
//
// Session tokens: Apple identity tokens are short-lived (~10 min) and not
// adjustable client-side. The Function App mints its own session JWT
// (HS256) after validating the Apple token at `POST /v1/auth/session`,
// and `JwtAuthMiddleware` accepts only the session token on every other
// protected route. `SessionSigningKey` is the HMAC secret (KV-backed);
// rotating it invalidates every active session — by design, no per-session
// revocation table.
public sealed record AuthOptions
{
    public string AppleAudience { get; init; } = "";
    public string CosmosDatabase { get; init; } = "";
    public string CosmosAllowedUsersContainer { get; init; } = "";

    public string SessionSigningKey { get; init; } = "";
    public string SessionIssuer { get; init; } = "notify";
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromDays(30);
}
