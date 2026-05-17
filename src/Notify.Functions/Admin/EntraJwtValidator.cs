using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Notify.Functions.Admin;

// Validates an Entra ID access token issued for the admin app registration.
// On success returns the validated `AdminUser`; on failure returns null —
// the middleware decides 401 vs 403. Mirrors [[AppleJwtValidator]].
//
// Validation rules:
//   - iss = https://login.microsoftonline.com/{tenantId}/v2.0
//     (single-tenant — only tokens minted in our directory accepted)
//   - aud = AdminOptions.EntraAudience (app registration appId)
//   - exp present, not expired (5-min clock skew)
//   - signature verifies against a key in tenant's JWKS
//   - `roles` claim contains AdminOptions.RequiredRole — enforced here
//     rather than in middleware so a token without the role looks the
//     same as an invalid token to callers (no probing for app-role gates)
public sealed record AdminUser(string Sub, string PreferredUsername);

public sealed class EntraJwtValidator
{
    public const string IssuerFormat = "https://login.microsoftonline.com/{0}/v2.0";

    private readonly IEntraJwksProvider _jwks;
    private readonly IOptions<AdminOptions> _opts;
    private readonly JwtSecurityTokenHandler _handler;

    public EntraJwtValidator(IEntraJwksProvider jwks, IOptions<AdminOptions> opts)
    {
        _jwks = jwks;
        _opts = opts;
        _handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
    }

    public Task<AdminUser?> ValidateAsync(string bearerToken, CancellationToken ct)
    {
        var o = _opts.Value;
        return ValidateAsync(bearerToken, o.EntraTenantId, o.EntraAudience, o.RequiredRole, ct);
    }

    // Exposed for unit tests that override the audience/issuer without DI.
    public async Task<AdminUser?> ValidateAsync(string bearerToken, string tenantId, string audience, string requiredRole, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bearerToken)
            || string.IsNullOrWhiteSpace(tenantId)
            || string.IsNullOrWhiteSpace(audience)
            || string.IsNullOrWhiteSpace(requiredRole))
            return null;
        if (!_handler.CanReadToken(bearerToken))
            return null;

        IReadOnlyCollection<SecurityKey> keys;
        try
        {
            keys = await _jwks.GetSigningKeysAsync(ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = string.Format(IssuerFormat, tenantId),
            ValidateIssuer = true,
            ValidAudience = audience,
            ValidateAudience = true,
            IssuerSigningKeys = keys,
            ValidateIssuerSigningKey = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5),
        };

        try
        {
            var principal = _handler.ValidateToken(bearerToken, parameters, out _);
            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (string.IsNullOrWhiteSpace(sub))
                return null;

            // `roles` is a multi-valued claim. JwtSecurityTokenHandler exposes
            // it as one Claim per role.
            var hasRole = principal.FindAll("roles").Any(c => string.Equals(c.Value, requiredRole, StringComparison.Ordinal));
            if (!hasRole)
                return null;

            var name = principal.FindFirst("preferred_username")?.Value
                       ?? principal.FindFirst("upn")?.Value
                       ?? "";
            return new AdminUser(sub, name);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException or FormatException)
        {
            return null;
        }
    }
}
