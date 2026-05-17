namespace Notify.Functions.Admin;

// Admin-plane config. Separate from AuthOptions because the admin app uses
// Entra ID as its IdP, not Sign in with Apple — different issuer, different
// audience, different JWKS endpoint, different role-claim shape.
//
// EntraTenantId picks the issuer + JWKS URL (single-tenant: only tokens
// minted in *this* directory are accepted). Audience is the Entra app
// registration's `appId` (client id). RequiredRole gates the middleware:
// every admin call must carry a `roles` claim that contains this value.
//
// When EntraTenantId is empty, AdminAuthMiddleware short-circuits to 503 on
// every /admin/* route — so a fork that hasn't run the Entra bootstrap yet
// can't accidentally expose unauthenticated admin endpoints.
public sealed record AdminOptions
{
    public string EntraTenantId { get; init; } = "";
    public string EntraAudience { get; init; } = "";
    public string RequiredRole { get; init; } = "Admin";
}
